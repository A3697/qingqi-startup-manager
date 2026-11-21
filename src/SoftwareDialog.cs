using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QingQi
{
    public static class SoftwareCatalog
    {
        public static List<StartupItem> Scan()
        {
            var found = new List<StartupItem>(StartupReader.Scan());
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
                ReadShortcuts(Environment.GetFolderPath(folder), found);
            foreach (Process process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    ProcessInfo info = Native.GetInfo(process.Id);
                    if (info == null || !File.Exists(info.Path)) continue;
                    var item = StartupReader.ResolveFile(info.Path);
                    string description = FileVersionInfo.GetVersionInfo(info.Path).FileDescription;
                    if (!String.IsNullOrWhiteSpace(description)) item.Name = description;
                    item.Source = "正在运行"; found.Add(item);
                }
                catch (Exception) { /* A process can exit or deny access during enumeration. */ }
            }
            return found.Where(x => File.Exists(x.Path) && x.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !Native.SamePath(x.Path, Application.ExecutablePath)
                    && !x.Name.StartsWith("卸载") && !x.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(x.Path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Path + "\n" + x.Arguments, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private static void ReadShortcuts(string folder, List<StartupItem> found)
        {
            if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            try
            {
                foreach (string path in Directory.GetFiles(folder, "*.lnk"))
                    try { var item = StartupReader.ResolveFile(path); item.Source = "软件快捷方式"; found.Add(item); } catch (Exception) { }
                foreach (string child in Directory.GetDirectories(folder))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) ReadShortcuts(child, found);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    public sealed class SoftwareDialog : Form
    {
        public StartupItem Selected;
        private readonly DataGridView grid = Theme.Grid();
        private readonly TextBox search = new TextBox();
        private readonly Label hint = new Label();
        private readonly Button add;
        private List<StartupItem> items = new List<StartupItem>();
        private readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        public SoftwareDialog()
        {
            SuspendLayout(); Text = "添加软件"; Icon = Artwork.Main; Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Theme.Background; ClientSize = new Size(1000, 600); MinimumSize = new Size(800, 500); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
            foreach (int height in new[] { 42, 36 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); Controls.Add(layout);
            var searchBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86)); searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchBar.Controls.Add(new Label { Text = "搜索软件", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            search.Dock = DockStyle.Fill; search.Margin = new Padding(0, 5, 0, 5); search.AccessibleName = "按名称或程序路径搜索"; searchBar.Controls.Add(search); layout.Controls.Add(searchBar, 0, 0);
            hint.Text = "正在获取软件列表…"; hint.Dock = DockStyle.Fill; hint.TextAlign = ContentAlignment.MiddleLeft; hint.ForeColor = Theme.Ink; layout.Controls.Add(hint, 0, 1);
            grid.Columns.Add(new DataGridViewImageColumn { HeaderText = "", Width = 44, ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true });
            grid.Columns.Add(Theme.Column("软件", 270)); grid.Columns.Add(Theme.Column("来源", 150));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "程序路径", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            layout.Controls.Add(grid, 0, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
            add = Theme.Button("添加所选软件", delegate { Choose(); }, true); add.Enabled = false; footer.Controls.Add(add);
            footer.Controls.Add(Theme.Button("选择软件路径…", delegate { ChoosePath(); }, false));
            var cancel = Theme.Button("取消", delegate { DialogResult = DialogResult.Cancel; }, false); footer.Controls.Add(cancel); layout.Controls.Add(footer, 0, 3);
            CancelButton = cancel; AcceptButton = add;
            search.TextChanged += delegate { Bind(); };
            grid.SelectionChanged += delegate { add.Enabled = grid.SelectedRows.Count > 0; };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Choose(); };
            grid.MouseDown += delegate(object sender, MouseEventArgs e) { if (grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None) { grid.CurrentCell = null; grid.ClearSelection(); } };
            Shown += async delegate
            {
                try
                {
                    List<StartupItem> scanned = await Task.Run(() => SoftwareCatalog.Scan());
                    if (IsDisposed) return; items = scanned; Bind(); search.Focus();
                }
                catch (Exception e) { if (!IsDisposed) hint.Text = "列表读取失败，可使用“选择软件路径”：" + e.Message; }
            };
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(false); PerformLayout();
            using (Graphics graphics = CreateGraphics()) Theme.ScaleGrid(grid, graphics.DpiX / 96F);
        }
        private void Bind()
        {
            string query = search.Text.Trim(); grid.Rows.Clear();
            foreach (StartupItem item in items.Where(x => x.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || x.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Bitmap picture;
                if (!pictures.TryGetValue(item.Path, out picture)) { picture = Artwork.ForFile(item.Path); pictures[item.Path] = picture; }
                int row = grid.Rows.Add(picture, item.Name, item.Source, item.Path); grid.Rows[row].Tag = item;
            }
            grid.CurrentCell = null; grid.ClearSelection(); add.Enabled = false;
            hint.Text = "找到 " + grid.Rows.Count + " 个软件（快捷方式、已登记自启动和运行窗口）。列表中没有的软件可选择路径添加。";
        }
        private void Choose()
        { if (grid.SelectedRows.Count > 0) { Selected = (StartupItem)grid.SelectedRows[0].Tag; DialogResult = DialogResult.OK; } }
        private void ChoosePath()
        {
            using (var pick = new OpenFileDialog { Filter = "软件或快捷方式|*.exe;*.lnk", CheckFileExists = true })
                if (pick.ShowDialog(this) == DialogResult.OK)
                    try { Selected = StartupReader.ResolveFile(pick.FileName); DialogResult = DialogResult.OK; } catch (Exception e) { Theme.Error(this, e); }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { foreach (Bitmap picture in pictures.Values) picture.Dispose(); pictures.Clear(); } base.Dispose(disposing); }
    }
}
