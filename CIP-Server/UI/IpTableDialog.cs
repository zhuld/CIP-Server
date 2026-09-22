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
        readonly DataGridView grid = new DataGridView();
        readonly Button btnAdd = new Button { Text = "添加", Width = 72 };
        readonly Button btnDel = new Button { Text = "删除", Width = 72 };
        readonly Button btnOk = new Button { Text = "确定", Width = 84 };
        readonly Button btnCancel = new Button { Text = "取消", Width = 84 };

        public IpTableDialog()
        {
            Text = "IP 表管理 (IPID → 设备名)";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(470, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            var colIpid = new DataGridViewTextBoxColumn { Name = "ipid", HeaderText = "IPID (十进制)", Width = 130 };
            var colName = new DataGridViewTextBoxColumn { Name = "name", HeaderText = "设备名称", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
            grid.Columns.Add(colIpid);
            grid.Columns.Add(colName);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            btnOk.Click += BtnOk_Click;
            btnDel.Click += delegate { if (grid.CurrentRow != null) grid.Rows.RemoveAt(grid.CurrentRow.Index); };
            btnAdd.Click += delegate { grid.Rows.Add(3, "新设备"); };
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnOk);
            bottom.Controls.Add(btnDel);
            bottom.Controls.Add(btnAdd);

            Controls.Add(grid);
            Controls.Add(bottom);
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

