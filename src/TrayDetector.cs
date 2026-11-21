using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QingQi
{
    public static class TrayDetector
    {
        [StructLayout(LayoutKind.Sequential)] private struct Identifier
        { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        public static bool HasIcon(string path)
        {
            var ids = new HashSet<uint>(); var guids = new List<Guid>();
            // Registry entries only supply identifiers. Shell verification excludes stale registrations.
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
                    if (root != null) foreach (string name in root.GetSubKeyNames())
                        using (RegistryKey key = root.OpenSubKey(name, false))
                        {
                            if (key == null || !Native.SamePath(Environment.ExpandEnvironmentVariables(key.GetValue("ExecutablePath") as string ?? ""), path)) continue;
                            object uid = key.GetValue("UID");
                            if (uid is int) ids.Add(unchecked((uint)(int)uid));
                            Guid guid; if (Guid.TryParse(Convert.ToString(uid), out guid)) guids.Add(guid);
                            if (Guid.TryParse(key.GetValue("IconGuid") as string, out guid)) guids.Add(guid);
                        }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            var pids = new HashSet<int>(Native.Find(path).Select(x => x.Id));
            if (pids.Count == 0) return false;
            foreach (Guid guid in guids) if (Exists(IntPtr.Zero, 0, guid)) return true;
            // Common ids also cover fresh registrations before Explorer writes its settings.
            for (uint i = 0; i <= 4; i++) ids.Add(i);
            bool found = false;
            Native.EnumWindows(delegate(IntPtr window, IntPtr state)
            { if (ForWindow(window, pids, ids)) { found = true; return false; } return true; }, IntPtr.Zero);
            if (found) return true;
            IntPtr after = IntPtr.Zero;
            for (int i = 0; i < 4096; i++)
            {
                after = FindWindowEx(new IntPtr(-3), after, null, null);
                if (after == IntPtr.Zero) break;
                if (ForWindow(after, pids, ids)) return true;
            }
            return false;
        }
        private static bool ForWindow(IntPtr window, HashSet<int> pids, HashSet<uint> ids)
        {
            int pid; Native.GetWindowThreadProcessId(window, out pid);
            if (!pids.Contains(pid)) return false;
            foreach (uint id in ids) if (Exists(window, id, Guid.Empty)) return true;
            return false;
        }
        private static bool Exists(IntPtr window, uint id, Guid guid)
        {
            var identifier = new Identifier { Size = (uint)Marshal.SizeOf(typeof(Identifier)), Window = window, Id = id, Guid = guid };
            Rect rect; return Shell_NotifyIconGetRect(ref identifier, out rect) == 0;
        }
    }
}
