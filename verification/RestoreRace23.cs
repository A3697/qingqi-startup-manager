using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QingQi;
class RestoreRace23
{
    static int Main(string[] args)
    {
        var store = new Storage(Path.Combine(args[0], "data"));
        if (args.Length > 1)
        {
            try
            {
                DateTime until = DateTime.UtcNow.AddSeconds(4);
                do
                {
                    if (String.IsNullOrEmpty(store.Read<WindowStatus>("state.json").SessionId)) throw new Exception("并发读取返回了空状态");
                    Thread.Sleep(1);
                } while (DateTime.UtcNow < until);
                return 0;
            }
            catch (Exception e) { Console.WriteLine(e); return 1; }
        }
        try
        {
            using (var mutex = new Mutex(false, @"Local\QingQi.Tray." + store.InstanceId()))
            {
                if (!mutex.WaitOne(0)) throw new Exception("测试锁已被占用");
                Task restore = Task.Run(delegate { Engine.Restore(store, new AppEntry { Id = "race", Path = Path.Combine(args[0], "NotRunning.exe") }); });
                Thread.Sleep(1200); mutex.ReleaseMutex();
                if (!restore.Wait(6500)) throw new Exception("恢复请求未在原锁释放后被接收");
            }
            if (Directory.GetFiles(store.FilePath("requests"), "*.json").Length != 0) throw new Exception("恢复请求被遗留");
            store.Write("state.json", new WindowStatus { SessionId = "seed" });
            using (var reader = Process.Start(new ProcessStartInfo(typeof(RestoreRace23).Assembly.Location, Engine.Quote(args[0]) + " read") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                for (int i = 0; i < 600; i++) store.Write("state.json", new WindowStatus { SessionId = i.ToString() });
                if (!reader.WaitForExit(15000)) throw new Exception("并发读取测试超时");
                if (reader.ExitCode != 0) throw new Exception("原子状态更新与读取冲突：" + reader.StandardOutput.ReadToEnd() + reader.StandardError.ReadToEnd());
            }
            store.Write("result.json", new { Passed = true, RestoreRequestSurvivesMutexExitRace = true, AtomicStatusConcurrentRead = true });
            Console.WriteLine("PASS: restore request exit race / concurrent atomic status reads"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
