// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/DarkTitleBar.cs
//  说明: 深色标题栏。通过 DwmSetWindowAttribute 告知 DWM 对窗口非客户区使用
//        深色配色（与本程序深色主题一致）：Win10 20H1+ 用属性 20，
//        1809/1903 用属性 19；系统不支持时调用返回错误码，静默忽略。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>深色标题栏助手（DWM 深色模式，仅 Win10 1809+ 生效）</summary>
    static class DarkTitleBar
    {
        /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE（Windows 10 20H1 及之后）</summary>
        const int DwmwaUseImmersiveDarkMode = 20;
        /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE（Windows 10 1809/1903 的早期编号）</summary>
        const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
            ref int attributeValue, int attributeSize);

        /// <summary>
        /// 为目标窗体启用深色标题栏。需在窗体句柄创建之后调用
        /// （建议放在 OnHandleCreated 中，此时 Handle 已存在且不会引起句柄重建）。
        /// </summary>
        public static void Apply(IWin32Window window)
        {
            if (window == null) return;
            try
            {
                IntPtr hwnd = window.Handle;
                if (hwnd == IntPtr.Zero) return;
                int on = 1;                       // 1 = 深色标题栏，0 = 浅色
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
            }
            catch (Exception)
            {
                // 无 DWM 的环境（旧系统/某些远程会话）忽略即可，标题栏保持系统默认
            }
        }
    }
}
