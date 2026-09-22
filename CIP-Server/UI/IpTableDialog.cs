// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/IpTableDialog.cs
//  说明: IP 表编辑对话框。表格内容在“确定”时由 CollectTable() 一次性写回
//        主窗体持有的 DeviceConfig，保存 config.json 或重启服务器后生效。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>IP 表编辑对话框：IPID → 设备名，决定注册接受/拒绝</summary>
    public sealed class IpTableDialog : Form
    {
        readonly BufferedGrid grid = new BufferedGrid();
        readonly FlatButton btnAdd = new FlatButton { Text = "添加", Width = 72 };
        readonly FlatButton btnDel = new FlatButton { Text = "删除", Width = 72 };
        readonly FlatButton btnOk = new FlatButton { Text = "确定", Width = 84, Kind = ButtonKind.Primary };
        readonly FlatButton btnCancel = new FlatButton { Text = "取消", Width = 84 };

        public IpTableDialog()
        {
            Text = "IP 表管理 (IPID → 设备名)";
            Font = UiTheme.FontBase;
            BackColor = UiTheme.WindowBack;
            DoubleBuffered = true;
            ClientSize = new Size(560, 430);
            Padding = new Padding(12);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // ---- 卡片 ----
            var card = UiTheme.Card(UiTheme.CardBack, UiTheme.WindowBack, 10);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.CardBack,
                Padding = new Padding(14, 12, 14, 12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.CardBack };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var title = UiTheme.SectionTitle("IP 表 (IPID → 设备名)");
            title.Margin = new Padding(0, 3, 0, 0);
            var hint = UiTheme.Caption("IPID 0-255 · 不在表内的设备以 FF FF 02 拒绝");
            hint.Margin = new Padding(0, 5, 0, 0);
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(hint, 1, 0);

            grid.Dock = DockStyle.Fill;
            grid.Margin = new Padding(0);
            UiTheme.StyleGrid(grid, false);
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ipid", HeaderText = "IPID (十进制)", FillWeight = 38 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "设备名称", FillWeight = 100 });

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = UiTheme.CardBack,
                Margin = new Padding(0)
            };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            btnOk.Click += BtnOk_Click;
            btnDel.Click += delegate { if (grid.CurrentRow != null) grid.Rows.RemoveAt(grid.CurrentRow.Index); };
            btnAdd.Click += delegate { grid.Rows.Add(3, "新设备"); };
            // 有意不设置 AcceptButton/CancelButton：Enter/Esc 需留给 DataGridView
            // 用于单元格编辑提交与编辑取消，否则对话框会被误关闭。
            bottom.Controls.Add(Center(btnCancel, 42));
            bottom.Controls.Add(Center(btnOk, 42));
            bottom.Controls.Add(Center(btnDel, 42));
            bottom.Controls.Add(Center(btnAdd, 42));

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            layout.Controls.Add(bottom, 0, 2);
            card.Controls.Add(layout);
            Controls.Add(card);
        }

        /// <summary>FlowLayoutPanel 不支持 Anchor，用 Margin 让按钮在行内垂直居中</summary>
        static Control Center(Control c, int rowHeight)
        {
            c.Margin = new Padding(6, Math.Max(0, (rowHeight - c.Height) / 2), 0, 0);
            return c;
        }

        /// <summary>用当前 IP 表填充行</summary>
        public void LoadTable(State.IpEntry[] entries)
        {
            grid.Rows.Clear();
            foreach (var e in entries)
                grid.Rows.Add(e.Ipid, e.Name ?? "");
        }

        void BtnOk_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>把表格内容写回 IP 表</summary>
        public State.IpEntry[] CollectTable()
        {
            var list = new System.Collections.Generic.List<State.IpEntry>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                int ipid;
                if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out ipid)) continue;
                if (ipid < 0 || ipid > 255) continue;
                list.Add(new State.IpEntry { Ipid = ipid, Name = Convert.ToString(row.Cells[1].Value) ?? "" });
            }
            return list.ToArray();
        }
    }
}

