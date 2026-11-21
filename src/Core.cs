using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace QingQi
{
    public enum WindowMode { Normal, Maximize, Minimize, Tray, AfterMinimize, AfterTray }
    public sealed class AppEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public bool Enabled = true;
        public string Name = "";
        public string Path = "";
        public string Arguments = "";
        public string WorkingDirectory = "";
        public string WindowPath = "";
        public bool FollowExisting = false;
        public int Priority = 10;
        public int DelaySeconds = 0;
        public WindowMode Mode = WindowMode.Normal;
        public int ShowSeconds = 2;
        public string CpuPriority = "Default";
        public string Source = "";
        public bool UseExistingTrayIcon;
        public bool HideToExistingTray;
        public string MatchPath { get { return String.IsNullOrWhiteSpace(WindowPath) ? Path : WindowPath; } }
    }
    public sealed class Settings
    {
        public int Version = 1;
        public bool ExitAfterRun = true;
        public int ObserveSeconds = 30;
        public List<AppEntry> Entries = new List<AppEntry>();
    }
    public sealed class WindowSession
    {
        public string Id = Guid.NewGuid().ToString("N");
        public DateTime UntilUtc;
        public bool Cancelled;
        public List<AppEntry> Entries = new List<AppEntry>();
    }
    public sealed class WindowStatus
    {
        public string SessionId = "";
        public bool Complete;
        public Dictionary<string, string> Items = new Dictionary<string, string>();
        public Dictionary<string, string> Errors = new Dictionary<string, string>();
        public List<string> Warnings = new List<string>();
        public string Failure = "";
    }
    public sealed class HiddenWindow
    {
        public long Handle;
        public int Pid;
        public long Created;
        public string Path = "";
        public string EntryId = "";
        public string Name = "";
        public bool ConfirmedHidden;
        public bool RestoreRequested;
        public bool ReleaseWhenRestored = true;
        public bool UseExistingTrayIcon;
        public bool NativeHandoff;
        public bool NativeRecovery;
    }
    public sealed class WindowRequest
    {
        public string Action = "restore";
        public string EntryId = "";
    }
    public sealed class ProcessInfo
    {
        public int Id;
        public string Path;
        public long Created;
    }
    public sealed class StartupItem
    {
        public string Name;
        public string Path;
        public string Arguments;
        public string WorkingDirectory;
        public string Source;
        public string State;
    }
    public sealed class Storage
    {
        public readonly string Root;
        private static readonly object Gate = new object();
        public Storage(string root)
        {
            string full = System.IO.Path.GetFullPath(root);
            string volume = System.IO.Path.GetPathRoot(full);
            Root = full.Length > volume.Length ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) : full;
            Directory.CreateDirectory(Root);
        }
        public string FilePath(string name) { return System.IO.Path.Combine(Root, name); }
        public T Read<T>(string name) where T : new()
        {
            string path = FilePath(name);
            lock (Gate)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        T value;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            value = new JavaScriptSerializer().Deserialize<T>(reader.ReadToEnd());
                        if (value == null) throw new InvalidDataException("配置为空，请检查 " + path);
                        return value;
                    }
                    catch (IOException error)
                    {
                        int code = error.HResult & 0xffff;
                        if (code == 2 && !File.Exists(path + ".bak")) return new T();
                        if (attempt >= 8 || (code != 2 && code != 32 && code != 33)) throw;
                        System.Threading.Thread.Sleep(20);
                    }
                }
            }
        }
        public void Write<T>(string name, T value)
        {
            lock (Gate)
            {
                string path = FilePath(name), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                        else File.Move(temp, path);
                        break;
                    }
                    catch (IOException error)
                    {
                        int code = error.HResult & 0xffff;
                        // A reader may still own the previous backup after an atomic replacement.
                        if (attempt >= 8 || (code != 32 && code != 33 && code != 1175)) throw;
                        System.Threading.Thread.Sleep(20);
                    }
                }
            }
        }
        public void Log(string text)
        {
            try
            {
                lock (Gate)
                {
                    string path = FilePath("运行日志.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 512000)
                    { File.Copy(path, path + ".previous", true); File.WriteAllText(path, ""); }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
        public string InstanceId()
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
        }
        public static Settings Validate(Settings s)
        {
            if (s == null || s.Version != 1 || s.Entries == null) throw new InvalidDataException("不支持的配置格式。保留原文件，没有覆盖。");
            if (s.ObserveSeconds < 1 || s.ObserveSeconds > 600 || s.Entries.Count > 100)
                throw new InvalidDataException("观察时间须为 1–600 秒，最多 100 个软件。");
            HashSet<string> ids = new HashSet<string>();
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppEntry e in s.Entries)
            {
                if (e == null || String.IsNullOrWhiteSpace(e.Id) || !ids.Add(e.Id)) throw new InvalidDataException("软件标识重复或为空。");
                if (!System.IO.Path.IsPathRooted(e.Path) || !e.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("软件路径必须是 EXE 的完整路径：" + e.Name);
                if (!paths.Add(System.IO.Path.GetFullPath(e.Path)))
                    throw new InvalidDataException("该软件已在列表中，请编辑现有条目：" + e.Name);
                if (!String.IsNullOrWhiteSpace(e.WindowPath) && (!System.IO.Path.IsPathRooted(e.WindowPath) || !e.WindowPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("窗口程序路径必须是 EXE 的完整路径。");
                if (e.Priority < 1 || e.Priority > 999 || e.DelaySeconds < 0 || e.DelaySeconds > 3600 || e.ShowSeconds < 1 || e.ShowSeconds > 120 || !Enum.IsDefined(typeof(WindowMode), e.Mode))
                    throw new InvalidDataException("软件参数超出范围：" + e.Name);
                if (!new[] { "Default", "Idle", "BelowNormal", "Normal", "AboveNormal", "High" }.Contains(e.CpuPriority))
                    throw new InvalidDataException("无效的进程优先级。");
            }
            return s;
        }
    }
    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr state);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hwnd);
        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(int pid, out int session);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr handle, uint priority);
        public static ProcessInfo GetInfo(int pid)
        {
            int session, own;
            using (Process current = Process.GetCurrentProcess()) own = current.SessionId;
            if (!ProcessIdToSessionId(pid, out session) || session != own) return null;
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                StringBuilder path = new StringBuilder(32768); int size = path.Capacity;
                long created, exited, kernel, user;
                if (!QueryFullProcessImageName(handle, 0, path, ref size) || !GetProcessTimes(handle, out created, out exited, out kernel, out user)) return null;
                return new ProcessInfo { Id = pid, Path = path.ToString(), Created = created };
            }
            finally { CloseHandle(handle); }
        }
        public static List<ProcessInfo> Find(string path)
        {
            var result = new List<ProcessInfo>();
            if (String.IsNullOrWhiteSpace(path)) return result;
            foreach (Process process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(path)))
            {
                using (process)
                {
                    try { ProcessInfo p = GetInfo(process.Id); if (p != null && SamePath(p.Path, path)) result.Add(p); }
                    catch (InvalidOperationException) { }
                }
            }
            return result;
        }
        public static bool SamePath(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        public static bool Matches(HiddenWindow record)
        {
            IntPtr hwnd = new IntPtr(record.Handle); int pid;
            if (!IsWindow(hwnd)) return false;
            GetWindowThreadProcessId(hwnd, out pid);
            ProcessInfo p = GetInfo(pid);
            return p != null && p.Id == record.Pid && p.Created == record.Created && SamePath(p.Path, record.Path);
        }
        public static List<IntPtr> WindowsFor(IEnumerable<int> ids)
        {
            var set = new HashSet<int>(ids); var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr state)
            {
                int pid; GetWindowThreadProcessId(hwnd, out pid);
                if (set.Contains(pid) && GetWindowTextLength(hwnd) > 0) list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }
        public static void SetCpu(ProcessInfo p, string priority)
        {
            if (priority == "Default") return;
            ProcessPriorityClass value = (ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass), priority);
            IntPtr handle = OpenProcess(0x0200 | 0x1000, false, p.Id);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("无法设置进程优先级，可能需要同等权限。");
            try { if (!SetPriorityClass(handle, (uint)value)) throw new InvalidOperationException("进程拒绝了优先级设置。"); }
            finally { CloseHandle(handle); }
        }
    }
    public static class StartupReader
    {
        // Read-only design informed by Faustvii/StartupManager (MIT); no third-party startup writes.
        public const string OwnName = "QingQiStartupManager";
        public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static StartupItem ParseCommand(string name, string command, string source)
        {
            string text = Environment.ExpandEnvironmentVariables(command ?? "").Trim();
            string exe = "", arguments = "";
            if (text.StartsWith("\""))
            {
                int end = text.IndexOf('"', 1); if (end < 0) return null;
                exe = text.Substring(1, end - 1); arguments = text.Substring(end + 1).Trim();
            }
            else
            {
                int end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (end < 0) return null;
                exe = text.Substring(0, end + 4); arguments = text.Substring(end + 4).Trim();
            }
            if (!System.IO.Path.IsPathRooted(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return new StartupItem { Name = name, Path = exe, Arguments = arguments, WorkingDirectory = System.IO.Path.GetDirectoryName(exe), Source = source, State = File.Exists(exe) ? "已注册" : "路径暂不可用" };
        }
        public static StartupItem ResolveFile(string path)
        {
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                object shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object linkObject = null;
                try
                {
                    dynamic shell = shellObject;
                    linkObject = shell.CreateShortcut(path); dynamic link = linkObject;
                    string target = (string)link.TargetPath;
                    var item = ParseCommand(System.IO.Path.GetFileNameWithoutExtension(path), "\"" + target + "\" " + (string)link.Arguments, path);
                    if (item == null) throw new InvalidDataException("该快捷方式没有指向可直接管理的 EXE。");
                    item.WorkingDirectory = String.IsNullOrWhiteSpace((string)link.WorkingDirectory) ? System.IO.Path.GetDirectoryName(target) : (string)link.WorkingDirectory;
                    return item;
                }
                finally
                {
                    if (linkObject != null) Marshal.FinalReleaseComObject(linkObject);
                    Marshal.FinalReleaseComObject(shellObject);
                }
            }
            return new StartupItem { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path, Arguments = "", WorkingDirectory = System.IO.Path.GetDirectoryName(path), Source = "手动选择", State = "" };
        }
        public static List<StartupItem> Scan()
        {
            var list = new List<StartupItem>();
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                // HKCU\Software\...\Run is shared between both WOW64 views.
                if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = root.OpenSubKey(RunPath, false))
                    using (RegistryKey approved = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + (view == RegistryView.Registry32 ? "Run32" : "Run"), false))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetValueNames())
                        {
                            if (name == OwnName || name == "FlydigiSpaceStationIndependentLauncher") continue;
                            StartupItem item = ParseCommand(name, key.GetValue(name) as string, (hive == RegistryHive.CurrentUser ? "当前用户 Run" : "所有用户 Run") + " · " + view.ToString());
                            if (item == null) continue;
                            byte[] state = approved == null ? null : approved.GetValue(name) as byte[];
                            if (state != null && state.Length > 0 && (state[0] == 3 || state[0] == 7)) item.State = "Windows 已禁用";
                            if (!list.Any(x => x.Source == item.Source && x.Name == item.Name)) list.Add(item);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                string root = Environment.GetFolderPath(folder);
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.GetFiles(root))
                {
                    if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    try { var item = ResolveFile(path); item.Source = "启动文件夹 · " + path; item.State = "文件存在（启用状态未判定）"; list.Add(item); }
                    catch { }
                }
            }
            ScheduledStartup.Read(list);
            return list.OrderBy(x => x.Name).ToList();
        }
        public static string OwnCommand { get { return "\"" + System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "轻启.exe") + "\" --startup"; } }
        public static bool OwnEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunPath, false))
            using (RegistryKey state = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", false))
            {
                if (key == null || !String.Equals(key.GetValue(OwnName) as string, OwnCommand, StringComparison.OrdinalIgnoreCase)) return false;
                byte[] value = state == null ? null : state.GetValue(OwnName) as byte[];
                return value == null || value.Length == 0 || (value[0] != 3 && value[0] != 7);
            }
        }
        public static void SetOwn(bool enabled, Storage store)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath))
            {
                object previous = key.GetValue(OwnName);
                if (previous != null && !String.Equals(previous as string, OwnCommand, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("轻启启动项指向其他位置，未覆盖。请先从原目录关闭自启动。");
                store.Write("自身启动项修改前.json", new { Time = DateTime.Now, Previous = previous, NewValue = enabled ? OwnCommand : null });
                if (enabled) key.SetValue(OwnName, OwnCommand, RegistryValueKind.String);
                else key.DeleteValue(OwnName, false);
            }
            if (enabled)
            {
                using (RegistryKey state = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                {
                    if (state != null && state.GetValue(OwnName) != null)
                    {
                        store.Write("自身启动批准状态备份.json", new { Original = state.GetValue(OwnName) });
                        state.DeleteValue(OwnName, false);
                    }
                }
            }
            store.Log(enabled ? "已启用轻启登录自启动。" : "已关闭轻启登录自启动。");
        }
    }
}
