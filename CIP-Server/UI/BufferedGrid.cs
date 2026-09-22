// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/BufferedGrid.cs
//  说明: 开启双缓冲的 DataGridView。客户端列表每秒刷新、join 网格 80ms 批量刷新，
//        默认 DataGridView 为单缓冲会出现闪烁，此处统一开启双缓冲与 WM_PAINT 合并。
// ============================================================================

using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>双缓冲 DataGridView（客户端列表 / join 监视网格通用）</summary>
    sealed class BufferedGrid : DataGridView
    {
        public BufferedGrid()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }
}
