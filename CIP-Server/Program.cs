// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Program.cs
//  说明: 应用程序入口。启用 WinForms 视觉样式后进入主窗体消息循环。
//  线程约定: Protocol/Server 层运行于网络/线程池线程，所有界面更新均通过
//            Control.BeginInvoke 封送至 UI 线程，勿在底层直接访问控件。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CipServerSim.UI;

namespace CipServerSim
{
    internal static class Program
    {
        /// <summary>应用程序入口点（STAThread：WinForms 的 COM 组件需要单线程单元）</summary>
        [STAThread]
        private static void Main()
        {
            EnsureDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// 确保进程按"系统 DPI 感知"运行（修复高 DPI 下被系统位图拉伸导致的界面模糊）。
        /// app.manifest 已声明 SystemAware，但实测（Windows 11 build 26200 + .NET Framework 4.8）
        /// 该清单声明未生效（进程停留在 Unaware）；DPI 感知以"先设置者为准"，故在进入
        /// WinForms 之前显式再设置一次，与清单互不冲突。优先 Win10 1607+ 的
        /// SetProcessDpiAwarenessContext(-2 = DPI_AWARENESS_CONTEXT_SYSTEM_AWARE)，
        /// 旧系统回退到 Vista 的 SetProcessDPIAware()（同为 SystemAware）。
        /// </summary>
        static void EnsureDpiAwareness()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-2)))
                    SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)   // Win10 1607 之前的系统无此导出
            {
                try { SetProcessDPIAware(); }
                catch (Exception) { /* 无法设置时保持默认，界面仍按 100% 渲染可用 */ }
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
    }
}
