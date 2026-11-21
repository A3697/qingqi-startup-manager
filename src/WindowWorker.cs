using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QingQi
{
    public sealed class WindowWorker : ApplicationContext
    {
        private readonly Storage store;
        private readonly Timer timer;
        private readonly Dictionary<string, NotifyIcon> icons = new Dictionary<string, NotifyIcon>();
        private DateTime lastIconCheck;
        private readonly Dictionary<string, Attempt> attempts = new Dictionary<string, Attempt>();
        private readonly HashSet<string> released = new HashSet<string>();
        private readonly HashSet<string> handoffs = new HashSet<string>();
        private readonly HashSet<string> priorities = new HashSet<string>();
        private readonly HashSet<long> restoredEvents = new HashSet<long>();
        private readonly Native.WinEventProc eventCallback;
        private readonly IntPtr eventHook;
        private List<HiddenWindow> hidden;
        private WindowSession session;
        private WindowStatus status = new WindowStatus();
        private DateTime lastSessionWrite, lastStatusWrite, lastProcessScan;
        private Dictionary<string, List<ProcessInfo>> processes = new Dictionary<string, List<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        private bool busy, dirty, ending, disposed, closeRequested, hiddenDirty;
        private sealed class Attempt
        {
            public DateTime First;
            public DateTime VisibleSince;
            public DateTime Sent;
            public bool Complete;
            public bool NativeTray;
            public HiddenWindow Record;
        }
        public WindowWorker(Storage storage)
        {
            store = storage;
            hidden = store.Read<List<HiddenWindow>>("托盘窗口.json").Where(Native.Matches).ToList();
            foreach (HiddenWindow record in hidden)
                if (!Native.IsWindowVisible(new IntPtr(record.Handle))) record.ConfirmedHidden = true;
            eventCallback = OnWindowEvent;
            // Out-of-context accessibility events, no DLL injection or modification of the target.
            eventHook = Native.SetWinEventHook(0x8002, 0x8003, IntPtr.Zero, eventCallback, 0, 0, 2);
            if (eventHook == IntPtr.Zero) store.Log("窗口事件通知不可用，将保守保留未确认的恢复记录。");
            timer = new Timer { Interval = 200 };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            store.Log("窗口任务开始；仅需轻启提供恢复图标时保留助手。");
        }
        private void OpenManager()
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "轻启.exe"), "--data-dir " + Engine.Quote(store.Root)) { UseShellExecute = true }); }
            catch (Exception e) { store.Log("打开界面失败：" + e.Message); }
        }
        private void SetStatus(string id, string message)
        {
            string old;
            if (!status.Items.TryGetValue(id, out old) || old != message)
            { status.Items[id] = message; dirty = true; }
        }
        private void SetError(string id, string message)
        { status.Errors[id] = message; SetStatus(id, message); dirty = true; }
        private void OnWindowEvent(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
        {
            if (objectId != 0 || childId != 0) return;
            try
            {
                foreach (HiddenWindow record in hidden.Where(x => x.Handle == hwnd.ToInt64()).ToList())
                {
                    if (!Native.Matches(record)) continue;
                    if (type == 0x8003) { record.ConfirmedHidden = true; hiddenDirty = true; }
                    if (type == 0x8002 && record.ConfirmedHidden) restoredEvents.Add(record.Handle);
                }
            }
            catch (Exception e) { store.Log("窗口事件核验失败：" + e.Message); }
        }
        private void PersistHidden() { store.Write("托盘窗口.json", hidden); }
        private void SyncIcons()
        {
            if ((DateTime.UtcNow - lastIconCheck).TotalSeconds < 2) return;
            lastIconCheck = DateTime.UtcNow;
            foreach (string id in icons.Keys.ToList()) if (!hidden.Any(x => x.EntryId == id)) RemoveIcon(id);
            foreach (var group in hidden.GroupBy(x => x.EntryId).ToList())
            {
                HiddenWindow record = group.First();
                if (group.All(x => x.NativeRecovery))
                {
                    if (TrayDetector.HasIcon(record.Path)) { RemoveIcon(record.EntryId); continue; }
                    // The previously verified recovery icon disappeared. Keep a fallback until restored.
                    foreach (HiddenWindow pending in group) { pending.NativeRecovery = false; pending.UseExistingTrayIcon = false; }
                    PersistHidden(); EnsureIcon(record); continue;
                }
                bool native = record.UseExistingTrayIcon || TrayDetector.HasIcon(record.Path);
                if (native)
                {
                    // An icon that appeared late needs a safe handoff. Keep only an already
                    // existing recovery icon until restoration is confirmed; never create one here.
                    if (!group.Any(x => x.RestoreRequested))
                    {
                        foreach (HiddenWindow pending in group) pending.NativeHandoff = true;
                        Restore(record.EntryId, false);
                        SetStatus(record.EntryId, "已发现原生托盘，正在交接窗口");
                    }
                    continue;
                }
                EnsureIcon(record);
            }
        }
        private void EnsureIcon(HiddenWindow record)
        {
            if (icons.ContainsKey(record.EntryId)) return;
            string id = record.EntryId;
            Icon picture; try { picture = Icon.ExtractAssociatedIcon(record.Path); } catch { picture = null; }
            if (picture == null) picture = (Icon)Artwork.Worker.Clone();
            string name = record.Name.Length > 40 ? record.Name.Substring(0, 40) : record.Name;
            var tray = new NotifyIcon { Icon = picture, Text = name + " · 点击恢复窗口" };
            var menu = new ContextMenuStrip();
            menu.Items.Add("恢复 " + record.Name, null, delegate { Restore(id); });
            menu.Items.Add("打开轻启管理界面", null, delegate { OpenManager(); });
            menu.Items.Add("恢复全部窗口并退出助手", null, delegate { ExitSafely(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Restore(id); };
            tray.Visible = true; icons.Add(id, tray);
            store.Log(record.Name + "：未检测到原生托盘，已提供恢复图标。");
        }
        private void RemoveIcon(string id)
        {
            NotifyIcon tray; if (!icons.TryGetValue(id, out tray)) return;
            icons.Remove(id); tray.Visible = false;
            Icon picture = tray.Icon; tray.ContextMenuStrip.Dispose(); tray.Dispose(); picture.Dispose();
        }
        private void Restore(string id) { Restore(id, true); }
        private void Restore(string id, bool release)
        {
            if (release) { released.Add(id); handoffs.RemoveWhere(x => x.StartsWith(id + ":")); }
            bool pending = false;
            foreach (HiddenWindow record in hidden.Where(x => x.EntryId == id).ToList())
            {
                if (!Native.Matches(record)) { hidden.Remove(record); continue; }
                IntPtr handle = new IntPtr(record.Handle);
                record.RestoreRequested = true; record.ReleaseWhenRestored = release;
                if (release) record.NativeHandoff = false;
                Native.ShowWindowAsync(handle, 9);
                // Retain the recovery record until a later tick confirms visibility.
                pending = true;
            }
            PersistHidden();
            SetStatus(id, pending ? "正在恢复窗口" : "窗口已恢复 / 已退出");
            store.Log("已请求恢复窗口：" + id);
        }
        private void LoadSession()
        {
            string path = store.FilePath("窗口任务.json");
            if (!File.Exists(path)) return;
            DateTime written = File.GetLastWriteTimeUtc(path);
            if (written == lastSessionWrite) return;
            WindowSession next = store.Read<WindowSession>("窗口任务.json");
            if (next.Entries == null || next.UntilUtc > DateTime.UtcNow.AddDays(5)) throw new InvalidDataException("无效的窗口管理任务。");
            Storage.Validate(new Settings { Entries = next.Entries });
            if (session == null || next.Id != session.Id)
            {
                // A native tray may have restored these windows while no worker was running.
                // They belong to the previous run and must not release the new run's rules.
                if (hidden.RemoveAll(x => x.ConfirmedHidden && Native.Matches(x) && Native.IsWindowVisible(new IntPtr(x.Handle))) > 0)
                    PersistHidden();
                attempts.Clear(); handoffs.Clear(); released.Clear(); priorities.Clear();
                closeRequested = false;
                status = new WindowStatus { SessionId = next.Id };
                dirty = true;
                foreach (AppEntry entry in next.Entries)
                {
                    foreach (HiddenWindow record in hidden.Where(x => x.EntryId == entry.Id))
                    {
                        if (Native.SamePath(record.Path, entry.MatchPath)) record.UseExistingTrayIcon = entry.UseExistingTrayIcon;
                        else record.NativeHandoff = false;
                    }
                    SetStatus(entry.Id, entry.FollowExisting ? "等待原自启动" : "等待软件窗口");
                    if (hidden.Any(x => x.EntryId == entry.Id && (!Native.SamePath(x.Path, entry.MatchPath)
                        || (entry.Mode != WindowMode.Tray && entry.Mode != WindowMode.AfterTray))))
                    {
                        Restore(entry.Id, false);
                    }
                }
            }
            session = next; lastSessionWrite = written;
        }
        private void Requests()
        {
            string folder = store.FilePath("requests");
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                WindowRequest request = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<WindowRequest>(File.ReadAllText(file));
                if (request.Action == "restore") Restore(request.EntryId);
                File.Delete(file); // Only completed, app-generated messages in this data directory.
            }
        }
        private void ScanProcesses()
        {
            if ((DateTime.UtcNow - lastProcessScan).TotalMilliseconds < 600) return;
            processes.Clear();
            if (session != null)
                foreach (string path in session.Entries.Select(x => x.MatchPath).Distinct(StringComparer.OrdinalIgnoreCase))
                    processes[path] = Native.Find(path);
            lastProcessScan = DateTime.UtcNow;
        }
        private void Manage(AppEntry entry, bool discoverNew)
        {
            if (released.Contains(entry.Id)) return;
            if (hidden.Any(x => x.EntryId == entry.Id && !Native.SamePath(x.Path, entry.MatchPath)))
            { SetStatus(entry.Id, "正在恢复旧程序窗口"); return; }
            List<ProcessInfo> found;
            if (!processes.TryGetValue(entry.MatchPath, out found) || found.Count == 0) return;
            foreach (ProcessInfo p in found)
            {
                string key = p.Id + ":" + p.Created + ":" + entry.CpuPriority;
                if (priorities.Add(key))
                {
                    try { Native.SetCpu(p, entry.CpuPriority); }
                    catch (Exception e) { store.Log(entry.Name + "：" + e.Message); SetError(entry.Id, "进程优先级受限"); }
                }
            }
            if (entry.Mode == WindowMode.Normal) { SetStatus(entry.Id, "已运行 · 沿用软件窗口"); return; }
            var windows = Native.WindowsFor(found.Select(x => x.Id));
            // Keep verifying a discovered window even if the application clears its title when hiding.
            foreach (Attempt pending in attempts.Values.Where(x => !x.Complete && x.Record.EntryId == entry.Id && Native.Matches(x.Record)))
            {
                IntPtr handle = new IntPtr(pending.Record.Handle);
                if (!windows.Contains(handle)) windows.Add(handle);
            }
            if (windows.Count == 0) { SetStatus(entry.Id, "已运行 · 等待窗口"); return; }
            if ((entry.Mode == WindowMode.Tray || entry.Mode == WindowMode.AfterTray)
                && windows.All(x => !Native.IsWindowVisible(x)) && !attempts.Keys.Any(x => x.StartsWith(entry.Id + ":"))
                && TrayDetector.HasIcon(entry.MatchPath))
            { SetStatus(entry.Id, "已在软件原生托盘中"); return; }
            foreach (IntPtr hwnd in windows)
            {
                int pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                ProcessInfo process = found.FirstOrDefault(x => x.Id == pid);
                if (process == null) continue;
                string key = entry.Id + ":" + hwnd.ToInt64() + ":" + process.Created;
                Attempt attempt;
                bool visible = Native.IsWindowVisible(hwnd);
                if (!attempts.TryGetValue(key, out attempt))
                {
                    if (!visible || !discoverNew) continue;
                    attempt = new Attempt
                    {
                        First = DateTime.UtcNow,
                        Record = new HiddenWindow { Handle = hwnd.ToInt64(), Pid = pid, Created = process.Created, Path = process.Path, EntryId = entry.Id, Name = entry.Name, UseExistingTrayIcon = entry.UseExistingTrayIcon }
                    };
                    attempts.Add(key, attempt);
                    if ((entry.Mode == WindowMode.AfterMinimize || entry.Mode == WindowMode.AfterTray) && Native.IsIconic(hwnd))
                        Native.ShowWindowAsync(hwnd, 9);
                }
                if (attempt.Complete) continue;
                bool tray = entry.Mode == WindowMode.Tray || entry.Mode == WindowMode.AfterTray;
                bool delayed = entry.Mode == WindowMode.AfterMinimize || entry.Mode == WindowMode.AfterTray;
                if (delayed && attempt.VisibleSince == DateTime.MinValue)
                {
                    if (Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) attempt.VisibleSince = DateTime.UtcNow;
                    else
                    {
                        if ((DateTime.UtcNow - attempt.First).TotalSeconds > 3)
                        { attempt.Complete = true; SetError(entry.Id, "窗口未能展开，未执行延后收起"); }
                        else SetStatus(entry.Id, "等待窗口展开");
                        continue;
                    }
                }
                if (delayed && (DateTime.UtcNow - attempt.VisibleSince).TotalSeconds < entry.ShowSeconds)
                { SetStatus(entry.Id, "窗口已打开 · 等待 " + entry.ShowSeconds + " 秒"); continue; }
                if (attempt.Sent == DateTime.MinValue)
                {
                    // Give startup icons time to appear before creating a fallback icon.
                    if (tray && !attempt.NativeTray && (DateTime.UtcNow - attempt.First).TotalSeconds < 2) continue;
                    attempt.NativeTray = tray && (attempt.NativeTray || entry.UseExistingTrayIcon || TrayDetector.HasIcon(process.Path));
                    if (tray && !attempt.NativeTray && !hidden.Any(x => x.Handle == attempt.Record.Handle && x.Created == attempt.Record.Created))
                    {
                        hidden.Add(attempt.Record);
                        PersistHidden(); // Persist recovery before hiding.
                        EnsureIcon(attempt.Record);
                    }
                    int command = tray ? 0 : entry.Mode == WindowMode.Maximize ? 3 : 7;
                    // Let the application handle its own minimize command. Never send WM_CLOSE.
                    bool accepted = attempt.NativeTray
                        ? Native.PostMessage(hwnd, 0x0112, new IntPtr(0xF020), IntPtr.Zero)
                        : Native.ShowWindowAsync(hwnd, command);
                    attempt.Sent = DateTime.UtcNow;
                    if (!accepted)
                    {
                        store.Log(entry.Name + "：窗口未接受控制请求，等待状态核验。");
                        if (tray && !attempt.Record.ConfirmedHidden && Native.IsWindowVisible(hwnd))
                        { hidden.Remove(attempt.Record); PersistHidden(); }
                    }
                    continue;
                }
                bool verified = tray ? !Native.IsWindowVisible(hwnd) : entry.Mode == WindowMode.Maximize ? Native.IsZoomed(hwnd) : Native.IsIconic(hwnd);
                if (verified)
                {
                    attempt.Complete = true;
                    handoffs.Remove(key);
                    if (tray && (!attempt.NativeTray || attempt.Record.NativeRecovery)) { attempt.Record.ConfirmedHidden = true; PersistHidden(); }
                    string message = tray ? (attempt.Record.NativeRecovery ? "已收起到原生托盘 · 使用原图标恢复" : attempt.NativeTray ? "已由软件原生收起到托盘" : "已收起到轻启托盘") : entry.Mode == WindowMode.Maximize ? "已最大化" : "已最小化";
                    SetStatus(entry.Id, message);
                    store.Log(entry.Name + "：" + message);
                }
                else if ((DateTime.UtcNow - attempt.Sent).TotalSeconds > 3)
                {
                    if (attempt.NativeTray && !attempt.Record.NativeRecovery && entry.HideToExistingTray && TrayDetector.HasIcon(process.Path))
                    {
                        attempt.Record.NativeRecovery = true;
                        attempt.Record.NativeHandoff = false;
                        attempt.Record.RestoreRequested = false;
                        attempt.Record.ReleaseWhenRestored = true;
                        attempt.Record.ConfirmedHidden = false;
                        restoredEvents.Remove(attempt.Record.Handle);
                        if (!hidden.Contains(attempt.Record)) hidden.Add(attempt.Record);
                        PersistHidden(); // Retain exact-window recovery even after the worker exits.
                        bool accepted = Native.ShowWindowAsync(hwnd, 0);
                        if (!accepted && !attempt.Record.ConfirmedHidden && Native.IsWindowVisible(hwnd))
                        { hidden.Remove(attempt.Record); PersistHidden(); }
                        attempt.Sent = DateTime.UtcNow;
                        continue;
                    }
                    attempt.Complete = true;
                    handoffs.Remove(key);
                    // An accepted asynchronous hide may execute late; keep recovery record and icon.
                    SetError(entry.Id, attempt.NativeTray && !attempt.Record.NativeRecovery ? "原生最小化未收起到托盘；已保留任务栏窗口，请在软件设置中配置" : "窗口控制未生效（权限或软件限制）");
                    store.Log(entry.Name + "：窗口状态核验未通过。");
                }
            }
        }
        private void CleanRecords()
        {
            bool changed = hiddenDirty; hiddenDirty = false;
            foreach (HiddenWindow record in hidden.ToList())
            {
                bool valid = Native.Matches(record);
                bool visible = valid && Native.IsWindowVisible(new IntPtr(record.Handle));
                if (valid && !visible && !record.ConfirmedHidden) { record.ConfirmedHidden = true; changed = true; }
                if (!valid || (record.ConfirmedHidden && visible) || (valid && restoredEvents.Contains(record.Handle) && visible))
                {
                    bool handoff = valid && visible && record.NativeHandoff && !record.ReleaseWhenRestored && !released.Contains(record.EntryId) && !closeRequested && session != null && !session.Cancelled
                        && session.Entries.Any(x => x.Id == record.EntryId && (x.Mode == WindowMode.Tray || x.Mode == WindowMode.AfterTray));
                    if (handoff)
                    {
                        string key = record.EntryId + ":" + record.Handle + ":" + record.Created;
                        attempts[key] = new Attempt { First = DateTime.UtcNow, VisibleSince = DateTime.UtcNow.AddSeconds(-121), Record = record, NativeTray = true };
                        handoffs.Add(key); status.Complete = false; dirty = true;
                    }
                    else if (valid && (!record.RestoreRequested || record.ReleaseWhenRestored)) released.Add(record.EntryId);
                    hidden.Remove(record); restoredEvents.Remove(record.Handle); changed = true;
                    if (!hidden.Any(x => x.EntryId == record.EntryId))
                    { RemoveIcon(record.EntryId); if (!handoff) SetStatus(record.EntryId, "窗口已恢复 / 已退出"); }
                }
            }
            if (changed) PersistHidden();
        }
        private void Tick()
        {
            if (busy || ending) return;
            busy = true;
            try
            {
                LoadSession(); Requests(); CleanRecords(); SyncIcons();
                handoffs.RemoveWhere(x => !attempts.ContainsKey(x) || !Native.Matches(attempts[x].Record));
                foreach (string key in handoffs.Where(x => (DateTime.UtcNow - attempts[x].First).TotalSeconds > 8).ToList())
                {
                    Attempt pending = attempts[key]; pending.Complete = true; handoffs.Remove(key);
                    SetError(pending.Record.EntryId, "原生托盘交接未确认，请通过软件原生图标检查窗口设置");
                }
                bool discovering = session != null && !session.Cancelled && DateTime.UtcNow < session.UntilUtc;
                int pendingBudget = session == null || session.Entries.Count == 0 ? 8 : session.Entries.Max(x => (x.Mode == WindowMode.AfterTray || x.Mode == WindowMode.AfterMinimize) ? x.ShowSeconds : 0) + 8;
                bool finishing = session != null && !session.Cancelled && DateTime.UtcNow < session.UntilUtc.AddSeconds(pendingBudget) && attempts.Values.Any(x => !x.Complete && Native.Matches(x.Record));
                if (!closeRequested && session != null && !session.Cancelled && (discovering || finishing || handoffs.Count > 0))
                {
                    ScanProcesses();
                    foreach (AppEntry entry in session.Entries) Manage(entry, discovering);
                }
                else if (!status.Complete)
                {
                    status.Complete = true; dirty = true;
                    if (session != null && !session.Cancelled && !closeRequested)
                    {
                        foreach (AppEntry entry in session.Entries)
                            if (hidden.Any(x => x.EntryId == entry.Id && !Native.SamePath(x.Path, entry.MatchPath)))
                                SetError(entry.Id, "旧程序窗口恢复未确认，未应用新程序窗口规则");
                        foreach (AppEntry entry in session.Entries.Where(x => x.Mode != WindowMode.Tray && x.Mode != WindowMode.AfterTray))
                            if (hidden.Any(x => x.EntryId == entry.Id && x.RestoreRequested && Native.Matches(x)))
                                SetError(entry.Id, "窗口恢复未确认，已保留托盘恢复入口");
                    }
                    foreach (string id in status.Items.Keys.ToList())
                        if (status.Items[id].StartsWith("等待") || status.Items[id].Contains("等待窗口"))
                        { SetStatus(id, "观察结束 · 未发现运行窗口"); status.Warnings.Add(id); }
                }
                // Once only recovery icons remain, no process scanning is needed and polling can be slower.
                timer.Interval = status.Complete ? 1000 : 200;
                if (dirty && (DateTime.UtcNow - lastStatusWrite).TotalMilliseconds >= 300)
                { store.Write("窗口状态.json", status); lastStatusWrite = DateTime.UtcNow; dirty = false; }
                if (status.Complete && hidden.All(x => x.NativeRecovery && !x.RestoreRequested && x.ConfirmedHidden))
                {
                    // Do not retire on a cached icon result from before the observation deadline.
                    lastIconCheck = DateTime.MinValue; SyncIcons();
                    if (hidden.All(x => x.NativeRecovery && !x.RestoreRequested && x.ConfirmedHidden))
                    { store.Write("窗口状态.json", status); ending = true; ExitThread(); }
                }
            }
            catch (Exception e)
            {
                store.Log("窗口助手：" + e.Message);
                status.Complete = true; status.Failure = e.Message;
                try { store.Write("窗口状态.json", status); } catch { }
                if (hidden.Count == 0) { ending = true; ExitThread(); }
                else { lastSessionWrite = File.Exists(store.FilePath("窗口任务.json")) ? File.GetLastWriteTimeUtc(store.FilePath("窗口任务.json")) : DateTime.MinValue; }
            }
            finally { busy = false; }
        }
        private void ExitSafely()
        {
            closeRequested = true;
            foreach (string id in hidden.Select(x => x.EntryId).Distinct().ToList()) Restore(id);
            // Keep recovery icons until the windows really become visible or exit.
            status.Complete = true; store.Write("窗口状态.json", status);
            if (hidden.Count == 0) { ending = true; ExitThread(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Stop(); timer.Dispose();
                if (eventHook != IntPtr.Zero) Native.UnhookWinEvent(eventHook);
                foreach (string id in icons.Keys.ToList()) RemoveIcon(id);
                store.Log("窗口助手已退出。");
            }
            base.Dispose(disposing);
        }
    }
}
