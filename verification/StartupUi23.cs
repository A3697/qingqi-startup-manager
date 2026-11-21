using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using QingQi;
class StartupUi23
{
    static int Main(string[] args)
    {
        var store = new Storage(Path.Combine(args[0], "data"));
        store.Write("配置.json", new Settings { ExitAfterRun = false });
        string exe = Path.Combine(args[0], "轻启.exe");
        using (var main = Process.Start(new ProcessStartInfo(exe, "--startup --data-dir " + Engine.Quote(store.Root)) { UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                Thread.Sleep(1500);
                var hidden = Native.WindowsFor(new[] { main.Id });
                if (hidden.Count == 0 || hidden.Any(Native.IsWindowVisible)) throw new Exception("登录启动时没有隐藏管理器窗口");
                DateTime ran = File.GetLastWriteTimeUtc(store.FilePath("最近运行.json"));
                if (new Storage(store.Root + Path.DirectorySeparatorChar).InstanceId() != store.InstanceId()) throw new Exception("目录尾分隔符导致实例标识不一致");
                using (var second = Process.Start(new ProcessStartInfo(exe, "--data-dir " + Engine.Quote(store.Root) + Path.DirectorySeparatorChar) { UseShellExecute = false, CreateNoWindow = true }))
                    if (!second.WaitForExit(5000) || second.ExitCode != 0) throw new Exception("重复实例没有退出");
                Thread.Sleep(1000);
                if (!Native.WindowsFor(new[] { main.Id }).Any(Native.IsWindowVisible)) throw new Exception("重新打开没有显示隐藏的管理器");
                if (File.GetLastWriteTimeUtc(store.FilePath("最近运行.json")) != ran) throw new Exception("打开界面重复执行规则");
                store.Write("result.json", new { Passed = true, StartupHidden = true, ReopenShowsExistingManager = true, ReopenDoesNotRerun = true, TrailingSeparatorSingleInstance = true });
                Console.WriteLine("PASS: hidden startup / reopen existing UI / no repeated run"); return 0;
            }
            catch (Exception e) { Console.WriteLine(e); return 1; }
            finally
            {
                foreach (IntPtr hwnd in Native.WindowsFor(new[] { main.Id })) Native.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (!main.WaitForExit(5000)) { main.Kill(); main.WaitForExit(); } // Only this isolated test child.
            }
        }
    }
}
