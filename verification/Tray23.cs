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
class Tray23
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] static int Main(string[] args)
    {
        string folder = args[0]; var store = new Storage(Path.Combine(folder, "data"));
        string[] names = { "Reuse", "OptinNoIcon", "LostIcon", "LateReuse", "ExitIconLoss" };
        string[] flags = { "nativetray=0", "", "nativetray=0 trayuntil=6500", "nativetray=4500", "nativetray=0 trayuntil=13500" };
        var entries = new List<AppEntry>(); var targets = new List<Process>();
        DateTime start = DateTime.UtcNow; bool verified = false, exitLossRestored = false; string failure = "";
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(folder, names[i] + ".exe");
                entries.Add(new AppEntry { Id = names[i], Name = names[i], Path = path, Mode = WindowMode.Tray, UseExistingTrayIcon = names[i] != "LateReuse", HideToExistingTray = true });
                targets.Add(Process.Start(new ProcessStartInfo(path, Engine.Quote(folder) + " 27000 " + flags[i]) { UseShellExecute = true }));
            }
            store.Write("配置.json", new Settings { Entries = entries });
            store.Write("窗口任务.json", new WindowSession { Entries = entries, UntilUtc = DateTime.UtcNow.AddSeconds(14) });
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var worker = new WindowWorker(store))
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate
                {
                    try
                    {
                        double time = (DateTime.UtcNow - start).TotalSeconds;
                        if (!verified && time >= 12.8)
                        {
                            var records = store.Read<List<HiddenWindow>>("托盘窗口.json");
                            var reuse = records.Single(x => x.EntryId == "Reuse");
                            Check(reuse.NativeRecovery && reuse.ConfirmedHidden && !Native.IsWindowVisible(new IntPtr(reuse.Handle)), "原生图标补充隐藏未生效");
                            var lost = records.Single(x => x.EntryId == "LostIcon");
                            Check(!lost.NativeRecovery && !Native.IsWindowVisible(new IntPtr(lost.Handle)), "原图标消失未交给轻量助手");
                            IDictionary icons = (IDictionary)typeof(WindowWorker).GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(worker);
                            Check(icons.Count == 1 && icons.Contains("LostIcon"), "原图标重复或丢失恢复入口");
                            var state = store.Read<WindowStatus>("窗口状态.json");
                            Check(state.Errors.ContainsKey("OptinNoIcon") && !records.Any(x => x.EntryId == "OptinNoIcon"), "无实际图标时仍执行隐藏");
                            Check(!state.Errors.ContainsKey("Reuse"), "原生补充隐藏被错误报告失败");
                            var late = records.Single(x => x.EntryId == "LateReuse");
                            Check(late.NativeRecovery && late.ConfirmedHidden && !late.RestoreRequested && !late.NativeHandoff && !Native.IsWindowVisible(new IntPtr(late.Handle)), "延迟原图标补充隐藏保留了旧交接状态");
                            typeof(WindowWorker).GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null).Invoke(worker, new object[] { "LostIcon" });
                            verified = true;
                        }
                        if (!exitLossRestored && time >= 14.4)
                        {
                            IDictionary icons = (IDictionary)typeof(WindowWorker).GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(worker);
                            Check(icons.Contains("ExitIconLoss"), "退出前原图标消失后没有保留恢复图标");
                            typeof(WindowWorker).GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null).Invoke(worker, new object[] { "ExitIconLoss" });
                            exitLossRestored = true;
                        }
                        if (time > 18) throw new Exception("仅原生图标托管时助手未退出");
                    }
                    catch (Exception e) { failure = e.ToString(); timer.Stop(); Application.ExitThread(); }
                };
                timer.Start(); Application.Run(worker);
            }
            Check(verified && failure.Length == 0, failure);
            Check(targets.All(x => !x.HasExited), "目标程序被提前结束");
            var recordsAfterExit = store.Read<List<HiddenWindow>>("托盘窗口.json");
            Check(recordsAfterExit.Count == 2 && recordsAfterExit.All(x => x.NativeRecovery && !Native.IsWindowVisible(new IntPtr(x.Handle))), "退出助手后未保留恢复记录");
            var saved = recordsAfterExit.Single(x => x.EntryId == "Reuse");
            var changed = new AppEntry { Id = "Reuse", Name = "ChangedPath", Path = entries[1].Path, Mode = WindowMode.Tray, FollowExisting = true };
            store.Write("窗口任务.json", new WindowSession { Entries = new List<AppEntry> { changed }, UntilUtc = DateTime.UtcNow.AddSeconds(4) });
            Engine.StartWorker(store);
            DateTime changedUntil = DateTime.UtcNow.AddSeconds(5);
            bool changedHidden = false;
            while (DateTime.UtcNow < changedUntil)
            {
                var current = store.Read<List<HiddenWindow>>("托盘窗口.json");
                changedHidden = current.Any(x => x.EntryId == "Reuse" && x.Path == changed.Path && !x.NativeRecovery && x.ConfirmedHidden);
                if (changedHidden) break;
                Thread.Sleep(100);
            }
            Check(changedHidden && Native.IsWindowVisible(new IntPtr(saved.Handle)), "旧路径恢复记录影响新软件的托盘规则");
            Engine.Restore(store, changed);
            Engine.Restore(store, entries.Single(x => x.Id == "LateReuse"));
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && (!Native.IsWindowVisible(new IntPtr(saved.Handle)) || store.Read<List<HiddenWindow>>("托盘窗口.json").Count > 0)) Thread.Sleep(100);
            Check(Native.IsWindowVisible(new IntPtr(saved.Handle)) && !Native.IsIconic(new IntPtr(saved.Handle)), "助手退出后管理器未能恢复窗口");
            Check(store.Read<List<HiddenWindow>>("托盘窗口.json").Count == 0, "恢复成功后未移除记录");
            store.Write("result.json", new { Passed = true, NativeFallbackHidden = true, NoDuplicateIcons = true, ManualClaimAloneNeverHides = true, LostIconHasRecovery = true, LateNativeFallback = true, ExitIconLossProtected = exitLossRestored, WorkerExitsWithNativeHidden = true, DurableRestoreAfterWorkerExit = true, ChangedPathRestoresOldWindowFirst = true });
            Console.WriteLine("PASS: native reuse / no duplicate / missing icon guard / icon loss / worker exit / durable restore"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
        finally { foreach (var p in targets) { p.WaitForExit(25000); p.Dispose(); } }
    }
}
