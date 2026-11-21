using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using QingQi;

[assembly: AssemblyTitle("轻启 · 自启动管理器")]
[assembly: AssemblyDescription("独立管理软件启动顺序和窗口状态；保留软件原自启动。")]
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        Storage store = null;
        try
        {
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            int index = Array.IndexOf(args, "--data-dir");
            if (index >= 0) { if (index + 1 >= args.Length) throw new ArgumentException("缺少数据目录。"); data = args[index + 1]; }
            store = new Storage(data);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--enable-startup")) { StartupReader.SetOwn(true, store); return 0; }
            if (args.Contains("--disable-startup")) { StartupReader.SetOwn(false, store); return 0; }
            if (args.Contains("--validate")) { Storage.Validate(store.Read<Settings>("配置.json")); return 0; }
            int restore = Array.IndexOf(args, "--restore-entry");
            if (restore >= 0)
            {
                if (restore + 1 >= args.Length) throw new ArgumentException("缺少软件标识。");
                var entry = Storage.Validate(store.Read<Settings>("配置.json")).Entries.First(x => x.Id == args[restore + 1]);
                Engine.Restore(store, entry); return 0;
            }
            string kind = "Main";
#if TRAY_HELPER
            kind = "Tray";
#endif
            using (Mutex mutex = new Mutex(false, @"Local\QingQi." + kind + "." + store.InstanceId()))
            {
                bool held;
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held)
                {
#if !TRAY_HELPER
                    if (!args.Contains("--startup"))
                    {
                        uint message = Native.RegisterWindowMessage("QingQi.Show." + store.InstanceId());
                        foreach (IntPtr window in Native.WindowsFor(Native.Find(Application.ExecutablePath).Select(x => x.Id)))
                            Native.PostMessage(window, message, IntPtr.Zero, IntPtr.Zero);
                    }
#endif
                    return 0;
                }
                try
                {
#if TRAY_HELPER
                    using (var worker = new WindowWorker(store)) Application.Run(worker);
#else
                    using (var form = new MainForm(store, args.Contains("--startup"))) Application.Run(form);
#endif
                }
                finally { mutex.ReleaseMutex(); }
            }
            return Environment.ExitCode;
        }
        catch (Exception e)
        {
            if (store != null) store.Log("程序失败：" + e.ToString());
#if !TRAY_HELPER
            if (!args.Contains("--startup")) MessageBox.Show(e.Message, "轻启启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            return 1;
        }
    }
}
