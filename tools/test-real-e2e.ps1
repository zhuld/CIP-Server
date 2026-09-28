# Real test.smw CIP end-to-end test
# Verifies XPanel IPID 0x04 -> SIMPL Interlock -> XPanel join feedback.
# Prerequisite: CIP-Server is running with tools/testdata/test.smw loaded.
# Usage: powershell -ExecutionPolicy Bypass -File tools/test-real-e2e.ps1
param(
    [string]$Server = "127.0.0.1",
    [int]$Port = 41794,
    [int]$Ipid = 4
)

$ErrorActionPreference = "Stop"
$script:failures = 0

function Check {
    param([string]$Label, [bool]$Ok, [string]$Detail = "")
    if ($Ok) {
        Write-Host ("  [PASS] {0}" -f $Label) -ForegroundColor Green
    } else {
        $script:failures++
        Write-Host ("  [FAIL] {0} {1}" -f $Label, $Detail) -ForegroundColor Red
    }
}

$client = New-Object System.Net.Sockets.TcpClient
$client.Connect($Server, $Port)
$stream = $client.GetStream()
$rx = New-Object System.Collections.Generic.List[byte]

function Read-For {
    param([double]$Seconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    $buf = New-Object byte[] 8192
    while ((Get-Date) -lt $deadline) {
        if ($stream.DataAvailable) {
            $n = $stream.Read($buf, 0, $buf.Length)
            for ($i = 0; $i -lt $n; $i++) { $rx.Add($buf[$i]) }
        } else {
            Start-Sleep -Milliseconds 25
        }
    }
}

function Send-Bytes {
    param([byte[]]$Data, [string]$Label)
    $stream.Write($Data, 0, $Data.Length)
    $stream.Flush()
    Write-Host ("  TX {0}" -f $Label)
}

function Rx-Hex {
    $parts = foreach ($b in $rx) { $b.ToString("X2") }
    ($parts -join " ")
}

function Rx-From {
    param([int]$Start)
    if ($Start -ge $rx.Count) { return "" }
    $parts = foreach ($i in $Start..($rx.Count - 1)) { $rx[$i].ToString("X2") }
    ($parts -join " ")
}

function Has-Bytes {
    param([string]$Text, [string]$Needle)
    if ($null -eq $Text) { return $false }
    return $Text.Contains($Needle)
}

Write-Host "=== 1. Connect and wait for Who-Is ==="
Read-For 2
Check "Who-Is received" (Has-Bytes (Rx-Hex) "0F 00 01 02")

Write-Host ("=== 2. Sign-On IPID 0x{0:X2} ===" -f $Ipid)
$signon = [byte[]](0x0A, 0x00, 0x0B, 0x00, [byte]$Ipid, 0xA3, 0x42, 0x40, 0x02, 0x00, 0x00, 0xD1, 0x01, 0x00)
Send-Bytes $signon "Sign-On"
Read-For 2
Check "IPID registration accepted" (Has-Bytes (Rx-Hex) "02 00 04 00 00 00 1F")

Write-Host "=== 3. Join 1 high: select Mode_1 ==="
$mark = $rx.Count
Send-Bytes ([byte[]](0x05, 0x00, 0x06, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00)) "digital join 1 high"
Read-For 2
$after1 = Rx-From $mark
Write-Host ("  RX: {0}" -f $after1)
Check "Mode_1_Fb join 1 high" (Has-Bytes $after1 "00 00 03 00 00 00")

Write-Host "=== 4. Join 1 low: interlock holds ==="
$mark = $rx.Count
Send-Bytes ([byte[]](0x05, 0x00, 0x06, 0x00, 0x00, 0x03, 0x00, 0x00, 0x80)) "digital join 1 low"
Read-For 2
$afterRelease1 = Rx-From $mark
Check "Mode_1_Fb did not fall" (-not (Has-Bytes $afterRelease1 "00 00 03 00 00 80"))

Write-Host "=== 5. Join 3 high: switch to Mode_3 ==="
$mark = $rx.Count
Send-Bytes ([byte[]](0x05, 0x00, 0x06, 0x00, 0x00, 0x03, 0x00, 0x02, 0x00)) "digital join 3 high"
Read-For 2
$after3 = Rx-From $mark
Write-Host ("  RX: {0}" -f $after3)
Check "Mode_1_Fb join 1 low" (Has-Bytes $after3 "00 00 03 00 00 80")
Check "Mode_3_Fb join 3 high" (Has-Bytes $after3 "00 00 03 00 02 00")

Write-Host "=== 6. Join 3 low: Mode_3 interlock holds ==="
$mark = $rx.Count
Send-Bytes ([byte[]](0x05, 0x00, 0x06, 0x00, 0x00, 0x03, 0x00, 0x02, 0x80)) "digital join 3 low"
Read-For 2
$afterRelease3 = Rx-From $mark
Check "Mode_3_Fb did not fall" (-not (Has-Bytes $afterRelease3 "00 00 03 00 02 80"))

Write-Host "=== 7. Update Request ==="
$mark = $rx.Count
Send-Bytes ([byte[]](0x05, 0x00, 0x05, 0x00, 0x00, 0x02, 0x03, 0x00)) "Update Request"
Read-For 2
$replay = Rx-From $mark
Write-Host ("  RX: {0}" -f $replay)
Check "Replay join 3 high" (Has-Bytes $replay "00 00 03 00 02 00")
Check "Replay update end" (Has-Bytes $replay "03 16")
Check "Replay update ready" (Has-Bytes $replay "03 1C")

$client.Close()
Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "REAL E2E PASSED" -ForegroundColor Green
    exit 0
}
Write-Host ("REAL E2E FAILED: {0} assertion(s)" -f $script:failures) -ForegroundColor Red
exit 1
