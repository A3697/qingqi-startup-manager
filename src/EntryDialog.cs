using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QingQi
{
    public sealed class EntryDialog : Form
    {
        public AppEntry Entry;
        private readonly TextBox name = new TextBox(), path = new TextBox(), args = new TextBox(), working = new TextBox(), windowPath = new TextBox();
        private readonly ComboBox cpu = new ComboBox();
        private readonly NumericUpDown priority = new NumericUpDown(), delay = new NumericUpDown(), seconds = new NumericUpDown();
        private readonly RadioButton[] sources = new RadioButton[2], modes = new RadioButton[6];
        private readonly ToggleSwitch nativeTray = new ToggleSwitch();
        private readonly ToggleSwitch reuseTray = new ToggleSwitch();
        private readonly List<StartupItem> startupItems;
        private readonly string originalPath;
        private string lastMatchPath;
        private readonly ToolTip hints = new ToolTip();
        private readonly Label outcome = new Label(), trayHint = new Label();
        private Bitmap picture;
        public EntryDialog(AppEntry original)
        {
            SuspendLayout();
            Entry = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AppEntry>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(original));
            startupItems = StartupReader.Scan();
            originalPath = original.Path;
            Text = "编辑软件规则 · " + original.Name; Icon = Artwork.Main;
            Font = new Font("Microsoft YaHei UI", 10F); BackColor = Theme.Background; ForeColor = Theme.Ink;
            ClientSize = new Size(820, 755); MinimumSize = new Size(800, 745); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 14), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); Controls.Add(root);
            picture = Artwork.ForFile(Entry.Path);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.Controls.Add(new PictureBox { Image = picture, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(4, 6, 8, 6) }, 0, 0);
            heading.Controls.Add(new Label { Text = Entry.Name, Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0); root.Controls.Add(heading, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill }; root.Controls.Add(tabs, 0, 1);
            var basic = new TabPage("启动方式与窗口") { BackColor = Theme.Background };
            var advanced = new TabPage("程序路径与高级设置") { BackColor = Theme.Background };
            tabs.TabPages.Add(basic); tabs.TabPages.Add(advanced);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), ColumnCount = 1, RowCount = 10 };
            foreach (int height in new[] { 26, 66, 44, 28, 198, 42, 38, 38, 38, 48 }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); basic.Controls.Add(body);
            body.Controls.Add(Heading("1  由谁启动软件"), 0, 0);
            var sourcePanel = Choices(2, 1); sources[0] = Choice("由轻启启动", "按下方顺序和等待时间启动"); sources[1] = Choice("沿用软件原自启动", "轻启只处理窗口，保留原启动项");
            sourcePanel.Controls.Add(sources[0], 0, 0); sourcePanel.Controls.Add(sources[1], 1, 0); sources[Entry.FollowExisting ? 1 : 0].Checked = true; body.Controls.Add(sourcePanel, 0, 1);
            priority.Minimum = 1; priority.Maximum = 999; priority.Value = Entry.Priority; priority.Width = 66;
            delay.Maximum = 3600; delay.Value = Entry.DelaySeconds; delay.Width = 66;
            seconds.Minimum = 1; seconds.Maximum = 120; seconds.Value = Entry.ShowSeconds; seconds.Width = 66;
            var timing = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            timing.Controls.Add(Caption("启动顺序")); timing.Controls.Add(priority); timing.Controls.Add(Caption("数字小的先启动    启动前等待")); timing.Controls.Add(delay); timing.Controls.Add(Caption("秒")); body.Controls.Add(timing, 0, 2);
            body.Controls.Add(Heading("2  启动后的窗口状态（蓝色为当前选择）"), 0, 3);
            string[] descriptions = { "保持软件自己的显示方式", "展开并占满屏幕", "保留任务栏按钮", "收起任务栏窗口，软件继续运行", "先显示一段时间，再收起到任务栏", "先显示一段时间，再收起到托盘" };
            var modePanel = Choices(2, 3);
            for (int i = 0; i < modes.Length; i++) { modes[i] = Choice(Theme.Modes[i], descriptions[i]); modePanel.Controls.Add(modes[i], i % 2, i / 2); }
            modes[(int)Entry.Mode].Checked = true; body.Controls.Add(modePanel, 0, 4);
            var dwell = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            dwell.Controls.Add(Caption("窗口实际展开后停留")); dwell.Controls.Add(seconds); dwell.Controls.Add(Caption("秒，再执行收起（仅后两种模式）")); body.Controls.Add(dwell, 0, 5);
            nativeTray.Checked = Entry.UseExistingTrayIcon;
            nativeTray.Text = "此软件自带托盘图标（手动指定，仅在自动识别遗漏时开启）";
            body.Controls.Add(nativeTray, 0, 6);
            reuseTray.Checked = Entry.HideToExistingTray;
            reuseTray.Text = "原生最小化无效时隐藏窗口（已确认原托盘图标能恢复）";
            body.Controls.Add(reuseTray, 0, 7);
            hints.SetToolTip(reuseTray, "仅在实际检测到原生图标时生效；不增加图标。请先确认该软件的原图标可以恢复隐藏窗口。更换窗口程序后需重新确认。");
            bool detected = TrayDetector.HasIcon(Entry.MatchPath);
            trayHint.Text = detected ? "已检测到原生托盘；优先由软件收起，已确认恢复后可开启上方补充处理。" : "自动检测原生图标；没有图标时，轻量助手为此软件提供恢复入口。";
            trayHint.Dock = DockStyle.Fill; trayHint.ForeColor = Color.FromArgb(29, 111, 103); trayHint.TextAlign = ContentAlignment.MiddleLeft; body.Controls.Add(trayHint, 0, 8);
            outcome.Dock = DockStyle.Fill; outcome.BackColor = Color.FromArgb(228, 236, 255); outcome.Padding = new Padding(8); outcome.ForeColor = Theme.Blue; body.Controls.Add(outcome, 0, 9);
            name.Text = Entry.Name; path.Text = Entry.Path; args.Text = Entry.Arguments; working.Text = Entry.WorkingDirectory; windowPath.Text = Entry.WindowPath;
            cpu.DropDownStyle = ComboBoxStyle.DropDownList; cpu.Items.AddRange(Theme.CpuNames); cpu.SelectedIndex = Math.Max(0, Array.IndexOf(Theme.Cpu, Entry.CpuPriority));
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8, AutoScroll = true };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); advanced.Controls.Add(fields);
            Field(fields, 0, "显示名称", name); Field(fields, 1, "启动程序", Browse(path)); Field(fields, 2, "启动参数", args); Field(fields, 3, "工作目录", working);
            Field(fields, 4, "窗口所属 EXE", Browse(windowPath)); Field(fields, 5, "CPU 优先级", cpu);
            var help = new Label { Dock = DockStyle.Fill, Text = "窗口所属 EXE：启动的是更新器时，填入真正拥有窗口的程序。一般留空。\r\nCPU 优先级影响运行调度，与启动顺序无关。一般沿用软件。\r\n这里的修改只保存到轻启，不改动目标软件或其原自启动设置。", ForeColor = Color.FromArgb(86, 102, 126) };
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); fields.Controls.Add(help, 0, 6); fields.SetColumnSpan(help, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var save = Theme.Button("保存此软件规则", SaveEntry, true); var cancel = Theme.Button("放弃修改", delegate { DialogResult = DialogResult.Cancel; }, false);
            footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 2); AcceptButton = save; CancelButton = cancel;
            foreach (RadioButton choice in sources.Concat(modes)) choice.CheckedChanged += delegate { UpdateEnabled(); };
            lastMatchPath = Entry.MatchPath;
            path.TextChanged += delegate { ResetTrayConfirmation(); UpdateSource(); };
            windowPath.TextChanged += delegate { ResetTrayConfirmation(); };
            UpdateSource();
            UpdateEnabled(); AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(false); PerformLayout();
        }
        private static Label Heading(string text) { return new Label { Text = text, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }; }
        private static Label Caption(string text) { return new Label { Text = text, AutoSize = true, Padding = new Padding(0, 3, 6, 0) }; }
        private static TableLayoutPanel Choices(int columns, int rows)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };
            for (int i = 0; i < columns; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            for (int i = 0; i < rows; i++) panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows)); return panel;
        }
        private static RadioButton Choice(string title, string description)
        {
            var choice = new RadioButton { Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 0, 8, 6) };
            choice.FlatAppearance.CheckedBackColor = Theme.Blue;
            Action update = delegate { choice.BackColor = !choice.Enabled ? Color.FromArgb(230, 233, 238) : choice.Checked ? Theme.Blue : Color.White; choice.ForeColor = !choice.Enabled ? SystemColors.GrayText : choice.Checked ? Color.White : Theme.Ink; choice.FlatAppearance.BorderColor = !choice.Enabled ? Color.FromArgb(177, 185, 198) : choice.Checked ? Theme.Blue : Theme.Border; choice.Text = (choice.Checked ? "✓ " : "○ ") + title + "\r\n" + description; };
            choice.CheckedChanged += delegate { update(); }; choice.EnabledChanged += delegate { update(); }; update(); return choice;
        }
        private void Field(TableLayoutPanel panel, int row, string title, Control control)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 9, 0, 7); panel.Controls.Add(control, 1, row);
        }
        private Control Browse(TextBox box)
        {
            var panel = new TableLayoutPanel { ColumnCount = 2 }; panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            box.Dock = DockStyle.Fill; var button = Theme.Button("浏览…", delegate { }, false); button.Dock = DockStyle.Fill; button.MinimumSize = Size.Empty; button.AutoSize = false;
            button.Click += delegate
            {
                using (var pick = new OpenFileDialog { Filter = "可执行程序|*.exe", CheckFileExists = true }) if (pick.ShowDialog(this) == DialogResult.OK) box.Text = pick.FileName;
            }; panel.Controls.Add(box, 0, 0); panel.Controls.Add(button, 1, 0); return panel;
        }
        private void UpdateEnabled()
        {
            if (!sources[1].Enabled) sources[1].Text = "沿用软件原自启动（不可选）\r\n未检测到已登记的原自启动项";
            priority.Enabled = delay.Enabled = sources[0].Checked;
            int selected = Array.FindIndex(modes, x => x != null && x.Checked); seconds.Enabled = selected == 4 || selected == 5;
            nativeTray.Enabled = reuseTray.Enabled = selected == 3 || selected == 5;
            outcome.Text = (sources[1].Checked ? "保留原自启动，只管理窗口。" : "由轻启按顺序启动。") + (selected >= 0 ? " 窗口：" + Theme.Modes[selected] : "");
        }
        private void UpdateSource()
        {
            StartupItem registered = startupItems.FirstOrDefault(x => Native.SamePath(x.Path, path.Text.Trim().Trim('"')));
            sources[1].Enabled = registered != null;
            if (registered == null && !Native.SamePath(originalPath, path.Text.Trim().Trim('"')) && sources[1].Checked) sources[0].Checked = true;
            if (registered == null) sources[1].Text = "沿用软件原自启动（不可选）\r\n未检测到已登记的原自启动项";
            hints.SetToolTip(sources[1], registered == null ? "未检测到原自启动登记。已有的跟随规则会保留，不会擅自改为主动启动。" : registered.Source + " · " + registered.State);
            UpdateEnabled();
        }
        private void ResetTrayConfirmation()
        {
            string match = String.IsNullOrWhiteSpace(windowPath.Text) ? path.Text : windowPath.Text;
            match = match.Trim().Trim('"');
            if (!Native.SamePath(lastMatchPath, match))
            {
                reuseTray.Checked = false; nativeTray.Checked = false; lastMatchPath = match;
                trayHint.Text = "窗口程序已变更：执行时重新检测原图标，恢复能力需要重新确认。";
            }
        }
        private void SaveEntry(object sender, EventArgs e)
        {
            try
            {
                Entry.Name = name.Text.Trim(); Entry.Path = path.Text.Trim().Trim('"'); Entry.Arguments = args.Text; Entry.WorkingDirectory = working.Text.Trim().Trim('"'); Entry.WindowPath = windowPath.Text.Trim().Trim('"');
                Entry.FollowExisting = sources[1].Checked; Entry.Priority = (int)priority.Value; Entry.DelaySeconds = (int)delay.Value; Entry.ShowSeconds = (int)seconds.Value;
                Entry.Mode = (WindowMode)Array.FindIndex(modes, x => x.Checked); Entry.CpuPriority = Theme.Cpu[cpu.SelectedIndex]; Entry.UseExistingTrayIcon = nativeTray.Checked;
                Entry.HideToExistingTray = reuseTray.Checked;
                if (String.IsNullOrWhiteSpace(Entry.Name)) throw new InvalidDataException("请填写软件名称。");
                Storage.Validate(new Settings { Entries = new List<AppEntry> { Entry } }); DialogResult = DialogResult.OK;
            }
            catch (Exception error) { Theme.Error(this, error); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { hints.Dispose(); if (picture != null) { picture.Dispose(); picture = null; } } base.Dispose(disposing); }
    }
}
