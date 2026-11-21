using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using QingQi;
class CustomTrayFixture
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Data
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, Mask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr Balloon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint command, ref Data data);
    [STAThread] static int Main(string[] args)
    {
        bool useGuid = args[0] == "guid", detected = false; DateTime start = DateTime.UtcNow;
        var form = new Form { Width = 20, Height = 20, ShowInTaskbar = false, Opacity = 0 };
        var data = new Data { Size = (uint)Marshal.SizeOf(typeof(Data)), Window = form.Handle, Id = 1001, Flags = (uint)(useGuid ? 38 : 6), Icon = SystemIcons.Information.Handle, Tip = "轻启隔离验证 " + args[0], Guid = useGuid ? Guid.NewGuid() : Guid.Empty };
        if (!Shell_NotifyIcon(0, ref data)) return 2;
        var timer = new Timer { Interval = 200 };
        timer.Tick += delegate { if ((DateTime.UtcNow - start).TotalSeconds > 2) { detected = TrayDetector.HasIcon(Application.ExecutablePath); form.Close(); } };
        timer.Start(); Application.Run(form); timer.Dispose(); Shell_NotifyIcon(2, ref data);
        Console.WriteLine(args[0] + " detected=" + detected); return detected ? 0 : 1;
    }
}
