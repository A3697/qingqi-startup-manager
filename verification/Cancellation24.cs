using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using QingQi;
class Cancellation24
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] static int Main(string[] args)
    {
        string root = args[0]; var store = new Storage(Path.Combine(root, "data"));
        Process target = null;
        try
        {
            var delayed = new AppEntry { Name = "Delayed", Path = Path.Combine(root, "Delayed.exe"), DelaySeconds = 5 };
            var disabled = new AppEntry { Name = "Disabled", Path = Path.Combine(root, "Missing.exe"), Enabled = false };
            using (var cancel = new CancellationTokenSource())
            {
                cancel.CancelAfter(300);
                var result = Engine.Run(new Settings { Entries = new List<AppEntry> { delayed, disabled } }, store, delegate { }, cancel.Token).GetAwaiter().GetResult();
                Check(result.Cancelled && result.Started == 0 && result.Failed == 0 && !File.Exists(Path.Combine(root, "Delayed.log")), "启动前等待取消未停止拉起");
            }
            var entry = new AppEntry { Id = "LateCancel", Name = "LateCancel", Path = Path.Combine(root, "LateCancel.exe"), Mode = WindowMode.Tray, HideToExistingTray = true };
            target = Process.Start(new ProcessStartInfo(entry.Path, Engine.Quote(root) + " 14000 nativetray=4500") { UseShellExecute = true });
            var session = new WindowSession { Entries = new List<AppEntry> { entry }, UntilUtc = DateTime.UtcNow.AddSeconds(12) };
            store.Write("窗口任务.json", session);
            bool cancelled = false, fallbackSeen = false; string failure = ""; IntPtr window = IntPtr.Zero;
            DateTime deadline = DateTime.UtcNow.AddSeconds(11);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var worker = new WindowWorker(store))
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate
                {
                    try
                    {
                        fallbackSeen |= store.Read<List<HiddenWindow>>("托盘窗口.json").Any(x => x.EntryId == entry.Id && x.ConfirmedHidden && !x.NativeRecovery);
                        var handoffs = (HashSet<string>)typeof(WindowWorker).GetField("handoffs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                        var attempts = (IDictionary)typeof(WindowWorker).GetField("attempts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                        if (!cancelled && handoffs.Count > 0)
                        {
                            object attempt = attempts[handoffs.First()];
                            DateTime sent = (DateTime)attempt.GetType().GetField("Sent").GetValue(attempt);
                            if (sent != DateTime.MinValue)
                            {
                                window = Native.WindowsFor(new[] { target.Id }).First();
                                session.Cancelled = true; store.Write("窗口任务.json", session); cancelled = true;
                            }
                        }
                        if (DateTime.UtcNow > deadline) throw new Exception("取消后助手未退出或未进入原生交接阶段");
                    }
                    catch (Exception e) { failure = e.ToString(); Application.ExitThread(); }
                };
                timer.Start(); Application.Run(worker);
            }
            Check(failure.Length == 0 && cancelled && fallbackSeen, "没有覆盖延迟图标交接取消：" + failure);
            Thread.Sleep(3500);
            Check(!target.HasExited && Native.IsWindowVisible(window), "取消后仍继续隐藏目标窗口");
            Check(store.Read<List<HiddenWindow>>("托盘窗口.json").Count == 0, "取消后遗留未恢复的窗口记录");
            var normal = new AppEntry { Path = entry.Path, Mode = WindowMode.Normal };
            var existing = Engine.Run(new Settings { Entries = new List<AppEntry> { normal, disabled } }, store, delegate { }, CancellationToken.None).GetAwaiter().GetResult();
            Check(existing.AlreadyRunning == 1 && existing.Started == 0 && existing.Failed == 0 && Native.IsWindowVisible(window), "正常窗口模式或禁用规则未保持原状态");
            store.Write("result.json", new { Passed = true, CancelBeforeLaunch = true, DisabledEntrySkipped = true, LateHandoffCancellation = true, CancelLeavesWindowAccessible = true, NormalModePreservesExistingWindow = true });
            Console.WriteLine("PASS: cancel delay / cancel native handoff / disabled entry / normal mode"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
        finally { if (target != null) { target.WaitForExit(16000); target.Dispose(); } }
    }
}
