using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using QingQi;
class RepeatNative23
{
    static int Main(string[] args)
    {
        string root = args[0]; var store = new Storage(Path.Combine(root, "data"));
        var entry = new AppEntry { Id = "repeat", Name = "Repeat native", Path = Path.Combine(root, "Fixture.exe"), FollowExisting = true, Mode = WindowMode.Tray, HideToExistingTray = true };
        var config = new Settings { Entries = new List<AppEntry> { entry }, ObserveSeconds = 1 };
        store.Write("配置.json", config);
        using (var target = Process.Start(new ProcessStartInfo(entry.Path, Engine.Quote(root) + " 22000 nativetray=0") { UseShellExecute = true }))
        {
            try
            {
                Thread.Sleep(800);
                for (int run = 0; run < 2; run++)
                {
                    RunResult result = Engine.Run(config, store, delegate { }, CancellationToken.None).GetAwaiter().GetResult();
                    var record = store.Read<List<HiddenWindow>>("托盘窗口.json").Single();
                    if (result.WindowFailures != 0 || !record.NativeRecovery || Native.IsWindowVisible(new IntPtr(record.Handle))) throw new Exception("第 " + (run + 1) + " 轮没有重新收起窗口");
                    Thread.Sleep(600); // Allow the helper to release its mutex after reporting completion.
                    Native.ShowWindowAsync(new IntPtr(record.Handle), 9); // Simulate the already verified original tray restore.
                    Thread.Sleep(300);
                    if (!Native.IsWindowVisible(new IntPtr(record.Handle))) throw new Exception("模拟原图标恢复失败");
                }
                store.Write("result.json", new { Passed = true, NativeRestoreBeforeNewRunDoesNotSuppressRule = true });
                Console.WriteLine("PASS: native hide / external restore / new run hides again"); return 0;
            }
            catch (Exception e) { Console.WriteLine(e); return 1; }
            finally { target.WaitForExit(25000); }
        }
    }
}
