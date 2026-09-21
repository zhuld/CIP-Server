# Crestron CIP 模拟客户端测试脚本
# 用法: powershell -ExecutionPolicy Bypass -File test-client.ps1 -Server 127.0.0.1 -Port 41794 -Ipid 3
# 流程: 连接 → 接收 Who-Is → Sign-On → 注册应答 → 数字 join 5 按下 → Update Request → 心跳

param(
    [string]$Server = "127.0.0.1",
    [int]$Port = 41794,
    [int]$Ipid = 3
)

$client = New-Object System.Net.Sockets.TcpClient
$client.Connect($Server, $Port)
$stream = $client.GetStream()
Write-Host "已连接 $Server`:$Port" -ForegroundColor Green

function Show-Rx {
    param([double]$seconds = 2)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $buf = New-Object byte[] 4096
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if ($stream.DataAvailable) {
            $n = $stream.Read($buf, 0, $buf.Length)
            if ($n -gt 0) {
                $hex = ($buf[0..($n - 1)] | ForEach-Object { $_.ToString("X2") }) -join " "
                Write-Host ("  RX {0} 字节: {1}" -f $n, $hex) -ForegroundColor Cyan
            }
        }
        else { Start-Sleep -Milliseconds 40 }
    }
}

function Send {
    param([byte[]]$data, [string]$label)
    $hex = ($data | ForEach-Object { $_.ToString("X2") }) -join " "
    Write-Host ("  TX {0}: {1}" -f $label, $hex) -ForegroundColor Yellow
    $stream.Write($data, 0, $data.Length)
}

Write-Host "=== 1. 等待 Who-Is (预期 0F 00 01 02) ==="
Show-Rx 3

Write-Host ("=== 2. 发送 Sign-On 短格式 (IPID=0x{0:X2}) ===" -f $Ipid)
$signon = New-Object byte[] 14
$signon[0] = 0x0A; $signon[1] = 0x00; $signon[2] = 0x0B; $signon[3] = 0x00; $signon[4] = $Ipid
$signon[5] = 0xA3; $signon[6] = 0x42; $signon[7] = 0x40; $signon[8] = 0x02; $signon[9] = 0x00
$signon[10] = 0x00; $signon[11] = 0xD1; $signon[12] = 0x01; $signon[13] = 0x00
Send $signon "Sign-On"
Write-Host "  (预期注册成功: 02 00 04 00 00 00 1F)"
Show-Rx 3

Write-Host "=== 3. 发送数字 join 5 按下(高电平) ==="
Send ([byte[]](0x05, 0x00, 0x06, 0x00, 0x00, 0x03, 0x00, 0x04, 0x00)) "数字 join 5 高"
Show-Rx 1

Write-Host "=== 4. 发送模拟 join 6 = 32768 ==="
Send ([byte[]](0x05, 0x00, 0x08, 0x00, 0x00, 0x05, 0x14, 0x00, 0x05, 0x80, 0x00)) "模拟 join 6"
Show-Rx 1

Write-Host "=== 5. 发送串口 join 7 = 'Hello CIP!' ==="
$text = [System.Text.Encoding]::UTF8.GetBytes("Hello CIP!")
$serial = [byte[]](0x05, 0x00, (4 + 3 + $text.Length), 0x00, 0x00, (3 + 1 + $text.Length), 0x15, 0x00, 0x06, 0x03) + $text
Send $serial "串口 join 7"
Show-Rx 1

Write-Host "=== 6. 发送 Update Request (预期回放 join 5/6/7 + 0x16 + 0x1C) ==="
Send ([byte[]](0x05, 0x00, 0x05, 0x00, 0x00, 0x02, 0x03, 0x00)) "Update Request"
Show-Rx 4

Write-Host "=== 7. 发送 Ping (预期 Pong 0E 00 02 00 00) ==="
Send ([byte[]](0x0D, 0x00, 0x02, 0x00, 0x00)) "Ping"
Show-Rx 2

$client.Close()
Write-Host "测试完成，连接已关闭。" -ForegroundColor Green
