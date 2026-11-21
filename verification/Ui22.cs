using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using QingQi;
internal static class Ui22
{
    private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] private static int Main(string[] args)
    {
        var store = new Storage(Path.Combine(args[0], "data")); var watch = Stopwatch.StartNew();
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var entries = StartupReader.Scan();
            Check(entries.Count > 0, "真实已登记自启动列表为空");
            StartupItem known = entries.First(x => File.Exists(x.Path));
            string unregistered = Path.Combine(args[0], "Unregistered.exe"); File.WriteAllBytes(unregistered, new byte[0]); // Path-only fixture; never execute.
            var original = new AppEntry { Name = "未登记测试项", Path = unregistered };
            using (var dialog = new EntryDialog(original))
            {
                Check(!Field<RadioButton[]>(dialog, "sources")[1].Enabled, "无登记项仍可选择原自启动");
                Check(Field<ToggleSwitch>(dialog, "nativeTray") != null, "兼容选项未使用滑轨");
            }
            using (var dialog = new EntryDialog(new AppEntry { Name = known.Name, Path = known.Path, FollowExisting = true, HideToExistingTray = true }))
            {
                var choices = Field<RadioButton[]>(dialog, "sources");
                Check(choices[1].Enabled && choices[1].Checked, "已登记项不能沿用原自启动");
                Check(Field<ToggleSwitch>(dialog, "reuseTray").Checked, "原图标恢复确认未保留");
                Field<TextBox>(dialog, "path").Text = unregistered;
                Check(!choices[1].Enabled && choices[0].Checked, "更换 EXE 后错误继承旧启动来源");
                Check(!Field<ToggleSwitch>(dialog, "reuseTray").Checked, "更换程序后错误沿用原托盘恢复确认");
                Field<ToggleSwitch>(dialog, "reuseTray").Checked = true;
                Field<ToggleSwitch>(dialog, "nativeTray").Checked = true;
                Field<TextBox>(dialog, "path").Text = known.Path;
                Check(!Field<ToggleSwitch>(dialog, "reuseTray").Checked && !Field<ToggleSwitch>(dialog, "nativeTray").Checked, "路径往返错误继承另一程序的托盘确认");
                Field<TextBox>(dialog, "windowPath").Text = unregistered;
                Field<ToggleSwitch>(dialog, "reuseTray").Checked = true;
                Field<TextBox>(dialog, "windowPath").Text = "";
                Check(!Field<ToggleSwitch>(dialog, "reuseTray").Checked, "窗口路径往返错误继承恢复确认");
            }
            using (var dialog = new EntryDialog(new AppEntry { Name = "历史跟随项", Path = unregistered, FollowExisting = true }))
            { var choices = Field<RadioButton[]>(dialog, "sources"); Check(!choices[1].Enabled && choices[1].Checked, "既有缺失来源规则被擅自转为主动启动"); }
            store.Write("配置.json", new Settings { Entries = new List<AppEntry> { original } });
            using (var form = new MainForm(store, false))
            {
                var grid = Field<DataGridView>(form, "grid");
                Check(grid.Columns[0].CellTemplate is ToggleCell, "列表启用项不是滑轨");
                grid.Rows[0].Cells[0].Value = false;
                Check(!store.Read<Settings>("配置.json").Entries[0].Enabled, "行开关未保存");
                grid.Rows[0].Cells[0].Value = true;
                grid.CurrentCell = grid.Rows[0].Cells[2]; grid.Rows[0].Selected = true;
                typeof(MainForm).GetMethod("ClearSelection", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                Check(grid.CurrentCell == null && grid.SelectedRows.Count == 0 && Field<List<Button>>(form, "selectedButtons").All(x => !x.Enabled), "取消选中后仍有活动操作");
                var exit = Field<ToggleSwitch>(form, "exitAfter"); exit.Checked = false;
                Check(!store.Read<Settings>("配置.json").ExitAfterRun, "全局滑轨未保存");
                var commit = typeof(MainForm).GetMethod("CommitEntry", BindingFlags.NonPublic | BindingFlags.Instance);
                var second = new AppEntry { Name = "第二测试项", Path = Path.Combine(args[0], "Second.exe") };
                commit.Invoke(form, new object[] { second });
                foreach (var duplicate in new[] { new AppEntry { Name = "新增重复", Path = unregistered }, new AppEntry { Id = second.Id, Name = "编辑重复", Path = unregistered } })
                {
                    bool rejected = false;
                    try { commit.Invoke(form, new object[] { duplicate }); }
                    catch (TargetInvocationException e) { rejected = e.InnerException is InvalidDataException; }
                    Check(rejected && Field<Settings>(form, "settings").Entries.Count == 2 && store.Read<Settings>("配置.json").Entries[1].Path == second.Path, "重复软件未拒绝或内存配置未回滚");
                }
                var refresh = typeof(MainForm).GetMethod("RefreshLog", BindingFlags.NonPublic | BindingFlags.Instance);
                string logPath = store.FilePath("运行日志.txt"); File.WriteAllText(logPath, "日志并发读取测试");
                using (var writer = new FileStream(logPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                { refresh.Invoke(form, null); Check(Field<TextBox>(form, "log").Text.Contains("日志并发读取测试"), "并发写入阻止日志读取"); }
                using (var locked = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                { refresh.Invoke(form, null); Check(Field<TextBox>(form, "log").Text.Contains("暂时不可读取"), "独占日志未显示可恢复提示"); }
            }
            List<StartupItem> catalog = SoftwareCatalog.Scan();
            Check(catalog.Count > 0 && catalog.Any(x => Native.SamePath(x.Path, known.Path)), "软件列表缺少原自启动软件");
            using (var dialog = new SoftwareDialog())
                Check(dialog != null, "添加软件弹窗构造失败");
            using (var dialog = new ImportDialog(new List<StartupItem> { new StartupItem { Name = "测试自启动", Path = unregistered, State = "已注册", Source = "测试" } }))
            {
                dialog.Show(); Application.DoEvents();
                var importGrid = Field<DataGridView>(dialog, "grid"); var add = (Button)dialog.AcceptButton;
                Check(!add.Enabled && importGrid.SelectedRows.Count == 0, "原自启动列表未选择时可以添加");
                importGrid.CurrentCell = importGrid.Rows[0].Cells[0]; importGrid.Rows[0].Selected = true;
                Check(add.Enabled, "选择原自启动后不能添加");
                importGrid.CurrentCell = null; importGrid.ClearSelection();
                Check(!add.Enabled, "原自启动取消选择后仍可添加"); dialog.Close();
            }
            store.Write("result.json", new { Passed = true, TogglePersistence = true, BlankDeselection = true, MissingStartupDisabled = true, RegisteredStartupSelectable = true, PathChangeResetsSource = true, PathRoundTripResetsTray = true, DuplicateAddAndEditRejected = true, LogConcurrentRead = true, LogExclusiveLockTolerated = true, OldFollowPreserved = true, StartupCount = entries.Count, TaskStartupCount = entries.Count(x => x.Source.StartsWith("登录/开机计划任务")), SoftwareCount = catalog.Count, Seconds = Math.Round(watch.Elapsed.TotalSeconds, 2) });
            Console.WriteLine(File.ReadAllText(store.FilePath("result.json"))); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
