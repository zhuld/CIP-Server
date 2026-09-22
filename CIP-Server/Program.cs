// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Program.cs
//  说明: 应用程序入口。启用 WinForms 视觉样式后进入主窗体消息循环。
//  线程约定: Protocol/Server 层运行于网络/线程池线程，所有界面更新均通过
//            Control.BeginInvoke 封送至 UI 线程，勿在底层直接访问控件。
// ============================================================================

using System;
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
