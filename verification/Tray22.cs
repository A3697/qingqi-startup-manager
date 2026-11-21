using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using QingQi;
internal static class Tray22
{
    [STAThread] private static int Main(string[] args)
    {
        string folder = args[0]; var store = new Storage(Path.Combine(folder, "data"));
        string[] names = { "Native", "NoNativeBehavior", "Missing", "Late", "LateBusy", "Manual", "LateBusyRelease" };
        string[] flags = { "nativetray=0 nativehide cleartitle", "nativetray=0", "", "nativetray=4500 nativehide cleartitle", "nativetray=4500 hangafter=4500:6000", "", "nativetray=4500 nativehide hangafter=4500:6000" };
        var entries = new List<AppEntry>();
        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(folder, names[i] + ".exe");
            entries.Add(new AppEntry { Id = names[i], Name = names[i], Path = path, Mode = WindowMode.Tray, UseExistingTrayIcon = names[i] == "Manual" });
            using (Process p = Process.Start(new ProcessStartInfo(path, Engine.Quote(folder) + " 21000 " + flags[i]) { UseShellExecute = true })) { }
        }
        store.Write("窗口任务.json", new WindowSession { Entries = entries, UntilUtc = DateTime.UtcNow.AddSeconds(14) });
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool first = false, handoff = false, restored = false; string failure = ""; DateTime start = DateTime.UtcNow;
        using (var worker = new WindowWorker(store))
        using (var timer = new Timer { Interval = 100 })
        {
            Func<IDictionary> icons = delegate { return (IDictionary)typeof(WindowWorker).GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(worker); };
            Action<string, bool> checkVisibility = delegate(string id, bool expected)
            {
                string handle = File.ReadAllLines(Path.Combine(folder, id + ".log")).First(x => x.StartsWith("HANDLE|")).Substring(7);
                IntPtr window = new IntPtr(Int64.Parse(handle));
                if (!Native.IsWindow(window) || Native.IsWindowVisible(window) != expected) throw new Exception(id + " 窗口可访问性错误");
            };
            timer.Tick += delegate
            {
                try
                {
                    double time = (DateTime.UtcNow - start).TotalSeconds;
                    if (!first && time >= 3.8)
                    {
                        IDictionary shown = icons();
                        if (shown.Count != 4 || !shown.Contains("Missing") || !shown.Contains("Late") || !shown.Contains("LateBusy") || !shown.Contains("LateBusyRelease")) throw new Exception("仅无原生图标的软件才应创建图标");
                        checkVisibility("Native", false); checkVisibility("NoNativeBehavior", true); checkVisibility("Manual", true);
                        ContextMenuStrip menu = ((NotifyIcon)shown["Missing"]).ContextMenuStrip;
                        menu.Show(new System.Drawing.Point(30, 30)); if (!menu.Visible) throw new Exception("首个恢复菜单不可用"); menu.Close();
                        first = true;
                    }
                    if (!handoff && time >= 8)
                    {
                        IDictionary shown = icons();
                        if (shown.Contains("Late") || !shown.Contains("LateBusy") || !shown.Contains("LateBusyRelease") || shown.Count != 3) throw new Exception("延迟图标交接必须保留尚未恢复窗口的入口");
                        checkVisibility("Late", false); checkVisibility("LateBusy", false);
                        WindowStatus state = store.Read<WindowStatus>("窗口状态.json");
                        if (!state.Errors.ContainsKey("NoNativeBehavior") || !state.Errors.ContainsKey("Manual")) throw new Exception("无法原生收起时必须提示配置");
                        typeof(WindowWorker).GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null).Invoke(worker, new object[] { "LateBusyRelease" });
                        handoff = true;
                    }
                    if (!restored && time >= 12.8)
                    {
                        IDictionary shown = icons();
                        if (shown.Count != 1 || !shown.Contains("Missing")) throw new Exception("恢复确认后必须释放交接图标");
                        checkVisibility("LateBusy", true);
                        checkVisibility("LateBusyRelease", true);
                        typeof(WindowWorker).GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null).Invoke(worker, new object[] { "Missing" });
                        restored = true;
                    }
                    if (time > 17) throw new Exception("仅原生托盘仍在运行时助手未退出");
                }
                catch (Exception e) { failure = e.ToString(); timer.Stop(); Application.ExitThread(); }
            };
            timer.Start(); Application.Run(worker);
        }
        bool pass = first && handoff && restored && failure.Length == 0 && store.Read<List<HiddenWindow>>("托盘窗口.json").Count == 0;
        store.Write("result.json", new { Passed = pass, NativeNoExtraIcon = first, OriginalMinimizeUsed = first, UnsupportedPreservesWindowAndReports = handoff, LateTraySafeHandoff = handoff && restored, UserRestoreCancelsHandoff = restored, EmptyTitleStillVerified = first && handoff, MissingIconRestored = restored, WorkerExitsWithNativeAppsStillRunning = pass, Failure = failure });
        Console.WriteLine(pass ? "PASS: native / unsupported / missing / delayed / hung handoff / exit" : failure); return pass ? 0 : 1;
    }
}
