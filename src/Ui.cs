using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QingQi
{
    public static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(30, 42, 65);
        public static readonly Color Blue = Color.FromArgb(54, 97, 224);
        public static readonly Color Border = Color.FromArgb(105, 123, 151);
        public static readonly Color Background = Color.FromArgb(244, 246, 250);
        public static readonly string[] Modes = { "正常显示（沿用软件）", "最大化", "最小化", "最小化到托盘", "打开窗口后最小化", "打开窗口后最小化到托盘" };
        public static readonly string[] Cpu = { "Default", "Idle", "BelowNormal", "Normal", "AboveNormal", "High" };
        public static readonly string[] CpuNames = { "沿用软件", "低", "低于正常", "正常", "高于正常", "高" };
        public static Button Button(string text, EventHandler action, bool primary)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(90, 34), Padding = new Padding(8, 1, 8, 1), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, Margin = new Padding(0, 0, 8, 0) };
            b.FlatAppearance.BorderColor = primary ? Blue : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(39, 77, 193) : Color.FromArgb(231, 238, 252);
            b.EnabledChanged += delegate
            {
                b.BackColor = !b.Enabled ? Color.FromArgb(230, 233, 238) : primary ? Blue : Color.White;
                b.ForeColor = !b.Enabled ? SystemColors.GrayText : primary ? Color.White : Ink;
                b.FlatAppearance.BorderColor = !b.Enabled ? Color.FromArgb(172, 182, 198) : primary ? Blue : Border;
            };
            b.Click += action; return b;
        }
        public static DataGridView Grid()
        {
            var grid = new BufferedGrid { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(237, 240, 246), ColumnHeadersHeight = 38 };
            grid.DefaultCellStyle.ForeColor = Ink; grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Blue; grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 239, 248);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.RowTemplate.Height = 40;
            return grid;
        }
        public static DataGridViewTextBoxColumn Column(string title, int width)
        { return new DataGridViewTextBoxColumn { Name = title, HeaderText = title, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable }; }
        public static void ScaleGrid(DataGridView grid, float scale)
        {
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) column.Width = (int)Math.Ceiling(column.Width * scale);
            grid.ColumnHeadersHeight = (int)Math.Ceiling(38 * scale);
            grid.RowTemplate.Height = (int)Math.Ceiling(40 * scale);
            foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
        }
        public static void Error(IWin32Window owner, Exception error) { MessageBox.Show(owner, error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    public sealed class ImportDialog : Form
    {
        public StartupItem Selected;
        private DataGridView grid = Theme.Grid();
        public ImportDialog(List<StartupItem> items)
        {
            SuspendLayout();
            Text = "添加已有自启动软件（保留原启动设置）"; Icon = Artwork.Main; Font = new Font("Microsoft YaHei UI", 10F); BackColor = Theme.Background;
            ClientSize = new Size(1020, 560); MinimumSize = new Size(920, 480); StartPosition = FormStartPosition.CenterParent;
            var label = new Label { Dock = DockStyle.Top, Height = 76, Padding = new Padding(14), Text = "选择后默认跟随原自启动，仅管理窗口。这里不会启用、禁用或改写原启动项。\r\n扫描 Run、启动文件夹和登录/开机计划任务；禁用的原启动项仍需在原软件或 Windows 中启用。" };
            grid.Columns.Add(Theme.Column("软件", 190)); grid.Columns.Add(Theme.Column("当前状态", 145)); grid.Columns.Add(Theme.Column("来源", 220));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "程序路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            foreach (StartupItem item in items) { int i = grid.Rows.Add(item.Name, item.State, item.Source, item.Path); grid.Rows[i].Tag = item; }
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(14, 9, 0, 0) };
            Action choose = delegate { if (grid.SelectedRows.Count > 0) { Selected = grid.SelectedRows[0].Tag as StartupItem; DialogResult = DialogResult.OK; } };
            var add = Theme.Button("添加所选软件", delegate { choose(); }, true); add.Enabled = false; footer.Controls.Add(add); AcceptButton = add;
            var cancel = Theme.Button("取消", delegate { DialogResult = DialogResult.Cancel; }, false); footer.Controls.Add(cancel); CancelButton = cancel;
            grid.SelectionChanged += delegate { add.Enabled = grid.SelectedRows.Count > 0; };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) choose(); };
            grid.MouseDown += delegate(object sender, MouseEventArgs e) { if (grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None) { grid.CurrentCell = null; grid.ClearSelection(); } };
            Shown += delegate { grid.CurrentCell = null; grid.ClearSelection(); };
            Controls.Add(grid); Controls.Add(footer); Controls.Add(label);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false); PerformLayout();
            using (Graphics graphics = CreateGraphics()) Theme.ScaleGrid(grid, graphics.DpiX / 96F);
        }
    }
    public sealed class MainForm : Form
    {
        private readonly Storage store;
        private Settings settings;
        private readonly bool startup;
        private readonly uint showMessage;
        private readonly DataGridView grid = Theme.Grid();
        private readonly ToggleSwitch ownStartup = new ToggleSwitch(), exitAfter = new ToggleSwitch();
        private readonly NumericUpDown observe = new NumericUpDown();
        private readonly Label summary = new Label(), status = new Label();
        private readonly Label selection = new Label();
        private readonly List<Button> selectedButtons = new List<Button>();
        private Button applySelected;
        private readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBox log = new TextBox();
        private readonly NotifyIcon tray;
        private readonly List<Button> editButtons = new List<Button>();
        private Button stop;
        private CancellationTokenSource cancellation;
        private bool binding, running, forceClose, closeAfterCancel, disposed, noticeOpened, noticeActive;
        private bool startupQueued, managerRequested;
        private Dictionary<string, string> messages = new Dictionary<string, string>();
        public MainForm(Storage storage, bool isStartup)
        {
            SuspendLayout();
            DoubleBuffered = true; ResizeRedraw = true;
            store = storage; settings = Storage.Validate(store.Read<Settings>("配置.json")); startup = isStartup;
            try
            {
                RunResult last = store.Read<RunResult>("最近运行.json"); messages = last.Status ?? new Dictionary<string, string>();
                WindowStatus current = store.Read<WindowStatus>("窗口状态.json");
                if (current.SessionId == last.SessionId)
                    foreach (var item in current.Items) { string error; messages[item.Key] = current.Errors.TryGetValue(item.Key, out error) ? error : item.Value; }
            }
            catch (Exception) { }
            showMessage = Native.RegisterWindowMessage("QingQi.Show." + store.InstanceId());
            Text = "轻启 · 自启动管理器 2.4"; Icon = Artwork.Main; Font = new Font("Microsoft YaHei UI", 10F); BackColor = Theme.Background; ForeColor = Theme.Ink;
            ClientSize = new Size(1140, 750); MinimumSize = new Size(1080, 710); StartPosition = FormStartPosition.CenterScreen;
            var layout = new BufferedTablePanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 103)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            var head = new BufferedPanel { Dock = DockStyle.Fill };
            head.Controls.Add(new Label { Text = "轻启", Font = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold), ForeColor = Theme.Blue, Location = new Point(0, 0), Size = new Size(120, 44) });
            head.Controls.Add(new Label { Text = "自启动管理器", Font = new Font("Microsoft YaHei UI", 15F), Location = new Point(119, 13), Size = new Size(220, 35) });
            summary.Location = new Point(3, 49); summary.Size = new Size(950, 24); summary.ForeColor = Color.FromArgb(100, 112, 132); head.Controls.Add(summary); layout.Controls.Add(head, 0, 0);
            var tools = new BufferedTablePanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            for (int i = 0; i < 4; i++) tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            AddButton(tools, "添加软件…", AddEntry, false); AddButton(tools, "添加已有自启动软件…", Import, false);
            AddButton(tools, "执行全部已启用规则", async delegate { await Execute(false); }, true);
            stop = Theme.Button("停止本轮后续操作", delegate { if (cancellation != null) cancellation.Cancel(); }, false); stop.Enabled = false; FitToolbarButton(stop); tools.Controls.Add(stop);
            layout.Controls.Add(tools, 0, 1);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "启用", Width = 72, CellTemplate = new ToggleCell() });
            grid.Columns.Add(new DataGridViewImageColumn { HeaderText = "", Width = 42, ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true });
            grid.Columns.Add(Theme.Column("软件", 200)); grid.Columns.Add(Theme.Column("启动方式", 130)); grid.Columns.Add(Theme.Column("启动顺序", 78));
            grid.Columns.Add(Theme.Column("启动前等待", 95)); grid.Columns.Add(Theme.Column("窗口模式", 222));
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "运行状态", HeaderText = "最近执行结果", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.SelectionChanged += delegate { if (!binding) UpdateSelection(); };
            grid.MouseDown += delegate(object sender, MouseEventArgs e) { if (grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None) ClearSelection(); };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (binding || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                try { var item = grid.Rows[e.RowIndex].Tag as AppEntry; item.Enabled = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value); Save(); UpdateSummary(); }
                catch (Exception error) { Theme.Error(this, error); }
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex != 0 && !running) Edit(null, EventArgs.Empty); };
            layout.Controls.Add(grid, 0, 2);
            var selected = new BufferedTablePanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(228, 236, 255), Padding = new Padding(12, 6, 12, 8), RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 8, 0, 0) };
            selected.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); selected.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selection.Dock = DockStyle.Fill; selection.Font = new Font(Font, FontStyle.Bold); selection.ForeColor = Theme.Blue; selection.TextAlign = ContentAlignment.MiddleLeft;
            selected.Controls.Add(selection, 0, 0);
            var actions = new BufferedTablePanel { Dock = DockStyle.Fill, BackColor = selected.BackColor, ColumnCount = 4, RowCount = 1 };
            for (int i = 0; i < 4; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            AddSelected(actions, "编辑此软件的规则…", Edit, true);
            AddSelected(actions, "立即应用此软件规则", async delegate { await Execute(true); }, false);
            applySelected = selectedButtons.Last();
            AddSelected(actions, "恢复此软件窗口", RestoreSelected, false);
            AddSelected(actions, "移除此软件的规则", Remove, false);
            selected.Controls.Add(actions, 0, 1); layout.Controls.Add(selected, 0, 3);
            var options = new BufferedTablePanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 12, 12, 9), ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 12, 0, 0) };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 278));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ownStartup.Text = "登录后执行规则";
            try { ownStartup.Checked = StartupReader.OwnEnabled(); }
            catch { ownStartup.Text = "自启动状态无读取权限"; ownStartup.Enabled = false; }
            ownStartup.CheckedChanged += delegate
            {
                if (binding) return;
                try { StartupReader.SetOwn(ownStartup.Checked, store); status.Text = ownStartup.Checked ? "轻启登录自启动已启用" : "轻启登录自启动已关闭"; }
                catch (Exception error) { Theme.Error(this, error); binding = true; ownStartup.Checked = StartupReader.OwnEnabled(); binding = false; }
            };
            exitAfter.Checked = settings.ExitAfterRun;
            exitAfter.Text = "执行完成后退出主程序";
            observe.Minimum = 1; observe.Maximum = 600; observe.Value = settings.ObserveSeconds; observe.Width = 66;
            var wait = new BufferedFlowPanel { Dock = DockStyle.Fill, BackColor = options.BackColor, WrapContents = false };
            wait.Controls.Add(new Label { Text = "窗口观察（秒）", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }); wait.Controls.Add(observe);
            wait.Controls.Add(new Label { Text = "自动保存", AutoSize = true, ForeColor = Theme.Blue, Padding = new Padding(6, 4, 0, 0) });
            exitAfter.CheckedChanged += delegate { SaveOptions(); };
            observe.ValueChanged += delegate { SaveOptions(); };
            options.Controls.Add(ownStartup, 0, 0); options.Controls.Add(exitAfter, 1, 0); options.Controls.Add(wait, 2, 0);
            var note = new Label { Dock = DockStyle.Fill, Text = "蓝色行表示选中；双击编辑，点击空白处取消。窗口观察时间：本轮继续等待新窗口出现的时长。\r\n已有托盘图标直接沿用；无原生图标的软件由轻量助手提供恢复入口。", ForeColor = Color.FromArgb(75, 88, 110), Padding = new Padding(0, 7, 0, 0) };
            options.Controls.Add(note, 0, 1); options.SetColumnSpan(note, 3); layout.Controls.Add(options, 0, 4);
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.BackColor = Theme.Background; log.BorderStyle = BorderStyle.None; log.ForeColor = Color.FromArgb(96, 108, 128); log.Margin = new Padding(2, 12, 0, 0); layout.Controls.Add(log, 0, 5);
            status.Text = "就绪 · 全局选项自动保存；编辑软件后点击“保存此软件规则”。"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.BottomLeft; layout.Controls.Add(status, 0, 6);
            foreach (Control blank in new Control[] { this, layout, head, summary, tools, selected, selection, actions, options, note, status })
                blank.MouseDown += delegate { ClearSelection(); };
            tray = new NotifyIcon { Icon = Artwork.Main, Text = "轻启 · 启动管理器", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开管理界面", null, delegate { ShowManager(); });
            menu.Items.Add("执行全部已启用规则", null, async delegate { await Execute(false); });
            menu.Items.Add("退出轻启", null, delegate { Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowManager(); };
            tray.BalloonTipClicked += delegate { noticeOpened = true; ShowManager(); };
            Bind(); RefreshLog();
            Shown += delegate { Invalidate(true); Update(); };
            ResizeEnd += delegate { Invalidate(true); Update(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!forceClose && running) { closeAfterCancel = true; cancellation.Cancel(); e.Cancel = true; }
                else if (!forceClose) { try { Save(); } catch (Exception error) { Theme.Error(this, error); e.Cancel = true; } }
            };
            if (startup) ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false); PerformLayout();
            using (Graphics graphics = CreateGraphics()) Theme.ScaleGrid(grid, graphics.DpiX / 96F);
        }
        private static void FitToolbarButton(Button button)
        { button.AutoSize = false; button.MinimumSize = Size.Empty; button.Dock = DockStyle.Fill; button.Padding = new Padding(2); }
        private void AddButton(TableLayoutPanel panel, string text, EventHandler action, bool primary)
        { Button b = Theme.Button(text, action, primary); FitToolbarButton(b); panel.Controls.Add(b); editButtons.Add(b); }
        private void AddSelected(TableLayoutPanel panel, string text, EventHandler action, bool primary)
        { AddButton(panel, text, action, primary); selectedButtons.Add(editButtons.Last()); }
        private void UpdateSelection()
        {
            AppEntry item = Current;
            selection.Text = item == null ? "选择一个软件，再设置或执行它的规则" : "当前选中：" + item.Name;
            foreach (Button button in selectedButtons) button.Enabled = item != null && !running;
            if (applySelected != null) applySelected.Text = item != null && item.FollowExisting ? "仅应用此软件窗口设置" : "启动并应用此软件窗口设置";
        }
        private void SaveOptions() { try { Save(); status.Text = "全局设置已自动保存"; } catch (Exception e) { Theme.Error(this, e); } }
        private AppEntry Current { get { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as AppEntry; } }
        private void ClearSelection() { grid.CurrentCell = null; grid.ClearSelection(); UpdateSelection(); }
        private void Save()
        {
            settings.ExitAfterRun = exitAfter.Checked; settings.ObserveSeconds = (int)observe.Value;
            store.Write("配置.json", Storage.Validate(settings));
        }
        private void Bind()
        {
            string id = Current == null ? "" : Current.Id;
            binding = true; grid.Rows.Clear(); DataGridViewRow chosen = null;
            foreach (AppEntry entry in settings.Entries.OrderBy(x => x.Priority))
            {
                string message; messages.TryGetValue(entry.Id, out message);
                Bitmap picture; if (!pictures.TryGetValue(entry.Path, out picture)) { picture = Artwork.ForFile(entry.Path); pictures[entry.Path] = picture; }
                int i = grid.Rows.Add(entry.Enabled, picture, entry.Name, entry.FollowExisting ? "沿用原自启动" : "由轻启启动", entry.FollowExisting ? "—" : entry.Priority.ToString(), entry.FollowExisting ? "—" : entry.DelaySeconds + " 秒", Theme.Modes[(int)entry.Mode], message ?? "尚未执行");
                grid.Rows[i].Tag = entry; grid.Rows[i].Cells[2].ToolTipText = entry.Path;
                if (entry.Id == id) chosen = grid.Rows[i];
            }
            grid.CurrentCell = null; grid.ClearSelection();
            if (chosen != null) { grid.CurrentCell = chosen.Cells[2]; chosen.Selected = true; }
            binding = false; UpdateSummary(); UpdateSelection();
        }
        private void UpdateSummary()
        { summary.Text = settings.Entries.Count + " 个软件   ·   " + settings.Entries.Count(x => x.Enabled) + " 个启用   ·   " + settings.Entries.Count(x => x.FollowExisting) + " 个保留原自启动"; }
        private void RefreshLog()
        {
            string file = store.FilePath("运行日志.txt");
            if (!File.Exists(file)) { log.Text = "运行记录将在这里显示。双击软件可编辑启动与窗口设置。"; return; }
            try
            {
                string text;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
                log.Text = text.Length > 18000 ? text.Substring(text.Length - 18000) : text; log.SelectionStart = log.TextLength; log.ScrollToCaret();
            }
            catch (IOException) { log.Text = "日志暂时不可读取，请稍后重新打开界面查看；启动与窗口处理不受影响。"; }
            catch (UnauthorizedAccessException) { log.Text = "日志读取权限不足；可在上方查看最近执行结果。"; }
        }
        private void AddEntry(object sender, EventArgs args)
        {
            using (var pick = new SoftwareDialog())
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var item = pick.Selected;
                    bool exists = StartupReader.Scan().Any(x => Native.SamePath(x.Path, item.Path));
                    EditNew(new AppEntry { Name = item.Name, Path = item.Path, Arguments = item.Arguments, WorkingDirectory = item.WorkingDirectory, Source = item.Source, FollowExisting = exists, Priority = NextPriority() });
                }
                catch (Exception e) { Theme.Error(this, e); }
            }
        }
        private int NextPriority() { return Math.Min(999, settings.Entries.Count == 0 ? 10 : settings.Entries.Max(x => x.Priority) + 10); }
        private void EditNew(AppEntry entry)
        {
            if (settings.Entries.Any(x => Native.SamePath(x.Path, entry.Path))) { Theme.Error(this, new Exception("该软件已在列表中，请编辑现有条目。")); return; }
            using (var dialog = new EntryDialog(entry))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { CommitEntry(dialog.Entry); } catch (Exception e) { Theme.Error(this, e); }
            }
        }
        private void Edit(object sender, EventArgs args)
        {
            if (Current == null) return;
            using (var dialog = new EntryDialog(Current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { CommitEntry(dialog.Entry); } catch (Exception e) { Theme.Error(this, e); }
            }
        }
        private void CommitEntry(AppEntry entry)
        {
            var previous = settings.Entries;
            var updated = new List<AppEntry>(previous);
            int index = updated.FindIndex(x => x.Id == entry.Id);
            if (index < 0) updated.Add(entry); else updated[index] = entry;
            settings.Entries = updated;
            try { Save(); }
            catch { settings.Entries = previous; throw; }
            Bind();
        }
        private void Import(object sender, EventArgs args)
        {
            try
            {
                using (var dialog = new ImportDialog(StartupReader.Scan()))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var item = dialog.Selected;
                    EditNew(new AppEntry { Name = item.Name, Path = item.Path, Arguments = item.Arguments, WorkingDirectory = item.WorkingDirectory, Source = item.Source, FollowExisting = true, Priority = NextPriority() });
                }
            }
            catch (Exception e) { Theme.Error(this, e); }
        }
        private void Remove(object sender, EventArgs args)
        {
            if (Current == null) return;
            try { AppEntry item = Current; Engine.Restore(store, item); settings.Entries.Remove(item); Save(); Bind(); status.Text = "已移出列表；软件和原自启动均保留。"; }
            catch (Exception e) { Theme.Error(this, e); }
        }
        private void RestoreSelected(object sender, EventArgs args)
        { if (Current != null) try { Engine.Restore(store, Current); status.Text = "已请求恢复窗口"; } catch (Exception e) { Theme.Error(this, e); } }
        private async Task Execute(bool selectedOnly)
        {
            if (running) return;
            if (selectedOnly && Current == null) return;
            noticeOpened = false;
            try { Save(); } catch (Exception e) { Theme.Error(this, e); return; }
            Settings run = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(settings));
            if (selectedOnly) { run.Entries = run.Entries.Where(x => x.Id == Current.Id).ToList(); run.Entries[0].Enabled = true; }
            running = true; cancellation = new CancellationTokenSource(); stop.Enabled = true; grid.ReadOnly = true;
            exitAfter.Enabled = observe.Enabled = false;
            foreach (Button b in editButtons) b.Enabled = false;
            status.Text = "正在按配置启动和管理窗口…";
            RunResult result = null;
            try
            {
                result = await Engine.Run(run, store, delegate(string id, string message)
                {
                    messages[id] = message;
                    foreach (DataGridViewRow row in grid.Rows) if (((AppEntry)row.Tag).Id == id) row.Cells["运行状态"].Value = message;
                }, cancellation.Token);
                status.Text = result.Cancelled ? "已取消剩余启动" : "完成 · 拉起 " + result.Started + " · 已运行 " + result.AlreadyRunning + " · 跟随 " + result.Followed + " · 启动失败 " + result.Failed + " · 窗口失败 " + result.WindowFailures + " · 未发现窗口 " + result.WindowWarnings;
                bool needsNativeSetup = result.Status.Values.Any(x => x.Contains("原生最小化未收起到托盘"));
                if (!selectedOnly && needsNativeSetup && run.ExitAfterRun && !Visible && !closeAfterCancel)
                {
                    noticeActive = true;
                    tray.ShowBalloonTip(8000, "轻启：软件需要配置原生托盘", "部分软件没有按原生方式收起到托盘，已保留任务栏窗口。点击查看具体软件与执行结果。", ToolTipIcon.Warning);
                    try { await Task.Delay(8000); } finally { noticeActive = false; }
                }
            }
            catch (Exception e)
            {
                store.Log("运行失败：" + e.Message); status.Text = "失败：" + e.Message;
                result = new RunResult { Failed = 1, CompletedAt = DateTime.Now };
                if (!startup) Theme.Error(this, e);
            }
            finally
            {
                running = false; cancellation.Dispose(); cancellation = null; stop.Enabled = false; grid.ReadOnly = false;
                exitAfter.Enabled = observe.Enabled = true;
                foreach (Button b in editButtons) b.Enabled = true; UpdateSelection(); RefreshLog();
            }
            if (closeAfterCancel || (!selectedOnly && result != null && !result.Cancelled && run.ExitAfterRun && !noticeOpened))
            { forceClose = true; Environment.ExitCode = result != null && (result.Failed > 0 || result.WindowFailures > 0) ? 1 : 0; Close(); }
        }
        protected override void SetVisibleCore(bool value)
        {
            // Start without ever creating a transparent/layered form. The tray command reveals it normally.
            base.SetVisibleCore(value && (!startup || managerRequested));
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (startup && !startupQueued)
            {
                startupQueued = true;
                BeginInvoke(new Action(async delegate { await Execute(false); }));
            }
        }
        private void ShowManager()
        {
            if (noticeActive) noticeOpened = true;
            managerRequested = true; ShowInTaskbar = true; Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Invalidate(true); Update(); Activate();
        }
        protected override void WndProc(ref Message m) { if (m.Msg == showMessage) ShowManager(); base.WndProc(ref m); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                foreach (Bitmap picture in pictures.Values) picture.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
