# CIP-Server — Crestron CIP 协议模拟服务器

> **AI 生成声明**：本项目代码由 AI（Cline）辅助生成。协议实现基于公开的逆向工程资料
> （python-cipclient、node-red-contrib-cip、rdriscoll/Crestron-CIP 等）分析编写，
> 非 Crestron 官方软件，与 Crestron Electronics 无任何关联，亦未获其认可或支持。
> CIP (Crestron over IP) 及相关商标归 Crestron Electronics, Inc. 所有。
> 本项目仅供协议学习、互操作性测试与开发调试使用。

一个运行于 Windows 的 Crestron CIP（Crestron over IP）协议服务器模拟器，
可模拟任意中控型号与 IPID，用于测试触摸屏、XPanel、Node-RED CIP 节点、
python-cipclient 等客户端程序，无需真实的 Crestron 处理器。

- 目标框架：.NET Framework 4.8（WinForms 图形界面）
- 解决方案：`CIP-Server.sln`（VS2019/2022 可直接打开，`dotnet build` 亦可构建）

## 功能

- **模拟中控型号**：型号/固件版本/主机名可配置（CP4、MC4、PRO3、RMC4 等任意字符串）
- **IPID 模拟**：内置 IP 表（IPID → 设备名），Sign-On 时校验；
  在表中 → 注册成功（`00 00 00 1F`），不在 → 拒绝（`FF FF 02`，与真实中控一致）
- **双端口监听**：
  - `41794` — CIP raw 消息协议（`[type][0x00][len][payload]`）
  - `41795` — Packetized CIP（`0D 00 [len] [type] [flag] [len2] [payload]`，触摸屏/XPanel 接入）
- **Join 状态引擎**：数字（bit 打包）、模拟（16 位大端）、串口（UTF-8 文本）的
  存储、实时广播、Update Request 全量回放（含 `0x16` 结束 / `0x1C` 就绪标记）
- **会话管理**：Who-Is → Sign-On → 注册 → Update 回放 → Ping/Pong 保活
  （10s 间隔，连续 5 次无应答断开）；同一 IPID 重复注册自动抢占旧连接
- **图形界面**：
  - 数字页：状态高亮（绿=高），双击行切换，支持置高/置低/脉冲 500ms
  - 模拟页：数值 + 进度条可视化，可直接下发数值
  - 串口页：显示最后收发文本、长度、时间，可发送文本
  - 客户端列表：远程地址、IPID、设备名、状态、协议模式、最后活动
  - 报文日志（窗口右侧面板）：十六进制 dump + 中文语义解析（TX/RX 分色），同时写入 `cip.log`
- **现代化界面**：深色主题 + 应用图标 + 深色标题栏（DWM），高 DPI 屏下按系统缩放清晰渲染
- **配置持久化**：`config.json`（界面保存 / IP 表对话框编辑）

## 使用

1. 打开 `CIP-Server.sln` 编译，或运行已构建的 `CIP-Server.exe`
2. 顶部设置型号、固件、端口，点击【启动服务器】
   （也可用命令行参数 `CIP-Server.exe autostart` 自动启动）
3. 客户端连接 41794（raw）或 41795（packetized），完成 Sign-On 后即可收发 join
4. 在数字/模拟/串口页操作，状态会实时推送到所有已注册客户端；
   客户端发来的 join 也会实时显示在界面上

## 界面技术要点（高 DPI / 深色标题栏 / 图标）

- **DPI 感知**：`Program.cs` 的 `EnsureDpiAwareness()` 在 `EnableVisualStyles()` 之前调用
  `SetProcessDpiAwarenessContext(-2)`（SystemAware，失败回退 `SetProcessDPIAware()`）。
  实测 `App.config` 的 `DpiAwareness` 键与嵌入式 `app.manifest` 在 Windows 11 (26200) +
  .NET Framework 4.8 下均未生效（进程停留在 Unaware、被系统位图拉伸导致模糊）；
  DPI 感知"先设置者为准"，故在入口显式设置（`app.manifest` 保留作声明与旧环境回退）。
- **缩放模型**：全部 UI 以 96dpi"设计像素"书写；窗体构建完成后 `UiTheme.ScaleToDpi()`
  （`Control.Scale`）统一放大边界 / Padding / Margin / 表格绝对行列，`UiTheme.Px()` 处理
  框架不缩放的度量（DataGridView 行高、ToolStrip、自绘圆角与内缩等）。
  注意：凡在 `ApplyDpiScale()` 中用 `Px()` 生成的物理常量（如 `MinimumSize`）必须在
  `Scale()` 之后赋值，否则会被二次放大。
- **深色标题栏**：`UI/DarkTitleBar.cs` 于 `OnHandleCreated` 调用 `DwmSetWindowAttribute`
  属性 20（`DWMWA_USE_IMMERSIVE_DARK_MODE`，旧系统回退 19），Win10 1809+ 生效、其他系统
  静默忽略；标题栏文字为白色（深色模式），是否显示强调色底纹跟随系统的"标题栏颜色"设置。
- **应用图标**：`app.ico` 含 16–256 多尺寸，csproj `ApplicationIcon` 内嵌到 exe；
  运行时 `Icon.ExtractAssociatedIcon` 显示于主窗体与 IP 表对话框标题栏。

## 命令行测试

```powershell
# 启动服务器后，运行模拟客户端脚本验证协议交互
powershell -ExecutionPolicy Bypass -File tools\test-client.ps1 -Server 127.0.0.1 -Port 41794 -Ipid 3
```

脚本流程：Who-Is → Sign-On → 数字/模拟/串口 join 下发 → Update Request 回放 → Ping/Pong。

## 配置文件（config.json）

```json
{
  "Model": "CP4",
  "Firmware": "v4.007.0014",
  "HostName": "CIP-SIM",
  "Port41794": 41794,
  "Port41795": 41795,
  "Enable41795": true,
  "DigitalJoins": 256,
  "AnalogJoins": 128,
  "SerialJoins": 64,
  "IpTable": [
    { "Ipid": 3, "Name": "TSW-1070 触摸屏" },
    { "Ipid": 4, "Name": "XPanel 2.0" }
  ]
}
```

## 协议要点（逆向工程摘要）

| 项目 | 说明 |
|---|---|
| 注册成功 | `02 00 04 00 00 00 1F` |
| IPID 不存在 | `02 00 03 FF FF 02` |
| Who-Is | `0F 00 01 02`（服务器接受连接后主动发送） |
| Sign-On 短格式 | `0A 00 0B 00 <IPID> …`（IPID 在 payload[1]） |
| Sign-On 长格式 | `01 00 0B 00 00 00 00 00 <IPID> 40 FF FF F1 01`（IPID 在 payload[5]） |
| Ping / Pong | `0D 00 02 00 00` / `0E 00 02 00 00` |
| Update Request | Join 事件，join 类型 `0x03`，数据 `0x00` |
| 数字 join | Join 事件 `0x00`：`[低8位][高7位|0x80(低电平)]`，join = (低 \| (高&0x7F)<<8) + 1 |
| 模拟 join | Join 事件 `0x14`：`[join-1 BE16][值 BE16]` |
| 串口 join | Join 事件 `0x15`：`[join-1 BE16][0x03][文本]` |
| Join 事件内层 | `[00 00 (dataLen+1) joinType data...]`，外层 `[05 00 innerLen]` |

## 已知限制

- SmartObject（0x38）、Dynamic List 等高级 Smart Graphics 对象仅记录日志，不模拟状态
- 41795 建立连接（establish）应答为通用 echo 实现，个别特殊客户端可能需微调
- 串口 join 以 UTF-8 编码传输（经典 CIP 为 Latin-1，ASCII 文本两者兼容）

## 许可证

本项目基于 [MIT License](LICENSE) 发布（Copyright (c) 2026 CIP-Server Contributors）。

- 代码由 AI（Cline）辅助生成，按“现状”提供，不含任何明示或默示的担保；
- 协议字节细节来源于社区逆向工程成果，可能随 Crestron 固件版本存在差异；
- 使用本项目即表示您已阅读并同意 LICENSE 文件中的附加说明。


## 项目结构

```
CIP-Server/
├── Program.cs                  入口（EnsureDpiAwareness + 消息循环）
├── app.ico                     应用图标（16–256 多尺寸，csproj ApplicationIcon 内嵌）
├── app.manifest                DPI 清单声明（声明/回退用，运行时以入口代码设置为准）
├── Protocol/                   协议层
│   ├── CipMessage.cs           消息类型码常量
│   ├── CipCodec.cs             消息构建（join 编码等）
│   ├── CipCodec.Parse.cs       解析 + 41795 packetized 帧
│   └── CipDescribe.cs          报文语义描述（日志用）
├── Server/
│   ├── CipServer.cs            TCP 监听、会话管理、join 广播
│   └── CipSession.cs           会话状态机（注册/心跳/回放）
├── State/
│   ├── JoinState.cs            join 状态存储（线程安全 + 事件）
│   └── DeviceConfig.cs         配置加载/保存（config.json）
├── Logging/Log.cs              十六进制 + 语义日志（控制台/cip.log）
└── UI/
    ├── MainForm.cs             主窗体（构建后 ScaleToDpi 统一缩放）
    ├── UiTheme.cs              主题色/字体/圆角绘制/Px() DPI 换算
    ├── DarkTitleBar.cs         深色标题栏（DwmSetWindowAttribute）
    ├── JoinPanelBase.cs        监视面板基类（节流刷新）
    ├── DigitalJoinPanel.cs     数字页
    ├── AnalogJoinPanel.cs      模拟页（进度条可视化）
    ├── SerialJoinPanel.cs      串口页
    ├── SegmentedTabs.cs        分页页签（自绘）
    ├── IpTableDialog.cs        IP 表编辑对话框
    ├── StatusPill.cs           状态胶囊（运行中/已停止）
    ├── FlatButton.cs           圆角扁平按钮
    ├── CardPanel.cs            圆角卡片面板
    ├── BufferedGrid.cs         双缓冲表格
    ├── LogViewState.cs         日志视图状态（暂停/过滤/搜索）
    └── FlatToolStripRenderer.cs 工具条自绘渲染器
```
