using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QingQi
{
    public sealed class RunResult
    {
        public string SessionId = "";
        public int Started;
        public int AlreadyRunning;
        public int Followed;
        public int Failed;
        public int WindowFailures;
        public int WindowWarnings;
        public string WindowError = "";
        public bool Cancelled;
        public DateTime CompletedAt;
        public Dictionary<string, string> Status = new Dictionary<string, string>();
    }
    public static class Engine
    {
        public static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        public static void StartWorker(Storage store)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "轻启托盘助手.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("缺少轻启托盘助手.exe，请保留完整便携目录。");
            using (Process process = Process.Start(new ProcessStartInfo(path, "--data-dir " + Quote(store.Root)) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
        }
        public static void Restore(Storage store, AppEntry entry)
        {
            var requests = new Storage(store.FilePath("requests"));
            string name = Guid.NewGuid().ToString("N") + ".json";
            requests.Write(name, new WindowRequest { EntryId = entry.Id });
            string request = requests.FilePath(name);
            // A worker may be releasing its mutex just as this request arrives. Confirm consumption.
            DateTime deadline = DateTime.UtcNow.AddSeconds(4), nextWake = DateTime.MinValue;
            while (File.Exists(request) && DateTime.UtcNow < deadline)
            {
                if (DateTime.UtcNow >= nextWake) { StartWorker(store); nextWake = DateTime.UtcNow.AddMilliseconds(750); }
                Thread.Sleep(50);
            }
            if (File.Exists(request)) throw new IOException("恢复请求尚未被助手接收，请稍后重试。恢复记录已保留。");
            foreach (IntPtr hwnd in Native.WindowsFor(Native.Find(entry.MatchPath).Select(x => x.Id)))
                if (Native.IsWindowVisible(hwnd)) Native.ShowWindowAsync(hwnd, 9);
        }
        public static async Task<RunResult> Run(Settings settings, Storage store, Action<string, string> progress, CancellationToken cancel)
        {
            Storage.Validate(settings);
            var result = new RunResult();
            var ordered = settings.Entries.Where(x => x.Enabled).OrderBy(x => x.Priority).ToList();
            var managed = ordered.Where(x => x.FollowExisting || x.Mode != WindowMode.Normal || x.CpuPriority != "Default").ToList();
            WindowSession session = null;
            if (managed.Count > 0)
            {
                session = new WindowSession { Entries = managed, UntilUtc = DateTime.UtcNow.AddSeconds(ordered.Where(x => !x.FollowExisting).Sum(x => x.DelaySeconds) + ordered.Count * 12 + settings.ObserveSeconds) };
                result.SessionId = session.Id;
                try { store.Write("窗口任务.json", session); StartWorker(store); }
                catch (Exception e)
                {
                    result.WindowFailures = 1; result.WindowError = "窗口助手启动失败：" + e.Message;
                    store.Log(result.WindowError); session = null;
                }
            }
            Action<AppEntry, string> report = delegate(AppEntry e, string message)
            { result.Status[e.Id] = message; progress(e.Id, message); store.Log(e.Name + "：" + message); };
            try
            {
                foreach (AppEntry entry in ordered)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (entry.FollowExisting)
                    { result.Followed++; report(entry, "保留原自启动 · 仅管理窗口"); continue; }
                    try
                    {
                        if (Native.Find(entry.Path).Count > 0 || (!Native.SamePath(entry.MatchPath, entry.Path) && Native.Find(entry.MatchPath).Count > 0))
                        { result.AlreadyRunning++; report(entry, "已运行 · 不重复拉起"); continue; }
                        if (entry.DelaySeconds > 0)
                        { report(entry, "等待 " + entry.DelaySeconds + " 秒"); await Task.Delay(entry.DelaySeconds * 1000, cancel); }
                        if (Native.Find(entry.Path).Count > 0 || (!Native.SamePath(entry.MatchPath, entry.Path) && Native.Find(entry.MatchPath).Count > 0))
                        { result.AlreadyRunning++; report(entry, "已自行启动 · 不重复拉起"); continue; }
                        if (!File.Exists(entry.Path)) throw new FileNotFoundException("软件路径不存在。");
                        string work = String.IsNullOrWhiteSpace(entry.WorkingDirectory) ? Path.GetDirectoryName(entry.Path) : entry.WorkingDirectory;
                        if (!Directory.Exists(work)) throw new DirectoryNotFoundException("工作目录不存在。");
                        ProcessWindowStyle style = ProcessWindowStyle.Normal;
                        if (entry.Mode == WindowMode.Maximize) style = ProcessWindowStyle.Maximized;
                        if (entry.Mode == WindowMode.Minimize || entry.Mode == WindowMode.Tray) style = ProcessWindowStyle.Minimized;
                        using (Process child = Process.Start(new ProcessStartInfo(entry.Path, entry.Arguments ?? "") { WorkingDirectory = work, UseShellExecute = true, WindowStyle = style }))
                        {
                            if (child == null) throw new InvalidOperationException("Windows 未返回启动进程。");
                            await Task.Delay(500, cancel);
                            if (child.HasExited && Native.Find(entry.Path).Count == 0 && Native.Find(entry.MatchPath).Count == 0)
                                throw new InvalidOperationException("启动进程很快退出；若它是更新器，请指定实际窗口程序。");
                        }
                        result.Started++; report(entry, "已拉起");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { result.Failed++; report(entry, "失败：" + e.Message); }
                }
                if (session != null)
                {
                    session.UntilUtc = DateTime.UtcNow.AddSeconds(settings.ObserveSeconds);
                    store.Write("窗口任务.json", session);
                    bool complete = false;
                    DateTime nextWake = DateTime.UtcNow.AddSeconds(1); int wakeRetries = 0;
                    int finishBudget = managed.Max(x => (x.Mode == WindowMode.AfterTray || x.Mode == WindowMode.AfterMinimize) ? x.ShowSeconds : 0) + 11;
                    while (DateTime.UtcNow < session.UntilUtc.AddSeconds(finishBudget))
                    {
                        cancel.ThrowIfCancellationRequested();
                        WindowStatus state = store.Read<WindowStatus>("窗口状态.json");
                        if (state.SessionId != session.Id && DateTime.UtcNow >= nextWake && wakeRetries < 3)
                        { StartWorker(store); wakeRetries++; nextWake = DateTime.UtcNow.AddSeconds(1); }
                        if (state.SessionId == session.Id)
                        {
                            foreach (var item in state.Items)
                            {
                                string message; if (!state.Errors.TryGetValue(item.Key, out message)) message = item.Value;
                                result.Status[item.Key] = message; progress(item.Key, message);
                            }
                            result.WindowFailures = state.Errors.Count + (String.IsNullOrEmpty(state.Failure) ? 0 : 1);
                            result.WindowWarnings = state.Warnings.Count;
                            result.WindowError = state.Failure;
                            if (state.Complete) { complete = true; break; }
                        }
                        await Task.Delay(300, cancel);
                    }
                    if (!complete)
                    {
                        result.WindowFailures++; result.WindowError = "窗口助手没有返回完成结果，请查看运行日志。";
                        store.Log(result.WindowError);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                if (session != null) { session.Cancelled = true; session.UntilUtc = DateTime.UtcNow; store.Write("窗口任务.json", session); }
                store.Log("已取消剩余启动；已运行的软件继续运行。");
            }
            result.CompletedAt = DateTime.Now;
            store.Write("最近运行.json", result);
            return result;
        }
    }
}
