using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using QingQi;
internal static class Notice22
{
    private static bool Flag(object form, string name) { return (bool)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form); }
    [STAThread] private static int Main(string[] args)
    {
        string folder = args[0]; var store = new Storage(Path.Combine(folder, "data"));
        var entry = new AppEntry { Id = "notice", Name = "原生托盘提示测试", Path = Path.Combine(folder, "NoticeTarget.exe"), Arguments = Engine.Quote(folder) + " 21000 nativetray=0", Mode = WindowMode.Tray };
        store.Write("配置.json", new Settings { ObserveSeconds = 2, ExitAfterRun = true, Entries = new List<AppEntry> { entry } });
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool noticed = false, kept = false; string failure = ""; DateTime started = DateTime.UtcNow;
        using (var form = new MainForm(store, true))
        using (var timer = new Timer { Interval = 100 })
        {
            timer.Tick += delegate
            {
                try
                {
                    if (Flag(form, "noticeActive") && !noticed)
                    {
                        if (!Flag(form, "running")) throw new Exception("提示等待期间提前解锁执行");
                        Task repeated = (Task)typeof(MainForm).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { false });
                        if (!repeated.IsCompleted) throw new Exception("提示等待期间启动了第二轮");
                        typeof(MainForm).GetMethod("ShowManager", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                        if (!form.Visible || !Flag(form, "noticeOpened")) throw new Exception("主动查看没有取消自动退出");
                        noticed = true;
                    }
                    if (noticed && !Flag(form, "noticeActive") && !Flag(form, "running"))
                    {
                        if (!form.Visible) throw new Exception("查看中的界面被旧通知计时关闭");
                        if (File.ReadAllLines(Path.Combine(folder, "NoticeTarget.log")).Count(x => x.StartsWith("START|")) != 1) throw new Exception("目标被重复拉起");
                        if (store.Read<List<HiddenWindow>>("托盘窗口.json").Count != 0) throw new Exception("原生失败保留了隐藏记录");
                        kept = true; timer.Stop(); form.Close();
                    }
                    if ((DateTime.UtcNow - started).TotalSeconds > 20) throw new Exception("通知生命周期超时");
                }
                catch (Exception e) { failure = e.ToString(); timer.Stop(); Application.ExitThread(); }
            };
            timer.Start(); Application.Run(form);
        }
        bool passed = noticed && kept && failure.Length == 0;
        store.Write("result.json", new { Passed = passed, NativeSetupNoticeRaised = noticed, ReentryBlocked = noticed, OpenedManagerPreserved = kept, TargetStartedOnce = kept, Failure = failure });
        Console.WriteLine(File.ReadAllText(store.FilePath("result.json"))); return passed ? 0 : 1;
    }
}
