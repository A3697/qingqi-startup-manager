using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class Fixture
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int mode);
    [STAThread] private static void Main(string[] args)
    {
        string root = args[0]; int lifetime = Int32.Parse(args[1]);
        string name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        DateTime start = DateTime.UtcNow;
        int hang = 0, selfShow = 0, hangAfter = 0, laterHang = 0, nativeTrayAt = -1; bool shownAgain = false, didHang = false, startMin = false, nativeHide = false, clearTitle = false;
        NotifyIcon ownTray = null; int nativeTrayUntil = -1; bool trayRemoved = false;
        foreach (string option in args)
        {
            if (option.StartsWith("hang=")) hang = Int32.Parse(option.Substring(5));
            if (option.StartsWith("selfshow=")) selfShow = Int32.Parse(option.Substring(9));
            if (option == "startmin") startMin = true;
            if (option == "nativehide") nativeHide = true;
            if (option == "cleartitle") clearTitle = true;
            if (option.StartsWith("nativetray=")) nativeTrayAt = Int32.Parse(option.Substring(11));
            if (option.StartsWith("trayuntil=")) nativeTrayUntil = Int32.Parse(option.Substring(10));
            if (option.StartsWith("hangafter="))
            { string[] parts = option.Substring(10).Split(':'); hangAfter = Int32.Parse(parts[0]); laterHang = Int32.Parse(parts[1]); }
        }
        string file = Path.Combine(root, name + ".log");
        File.AppendAllText(file, "START|" + start.ToString("o") + "|" + Environment.CurrentDirectory + "|" + Process.GetCurrentProcess().Id + "\n", Encoding.UTF8);
        Application.EnableVisualStyles();
        var form = new Form { Text = "轻启测试 · " + name, Width = 420, Height = 240, StartPosition = FormStartPosition.CenterScreen };
        form.Resize += delegate { if (nativeHide && ownTray != null && form.WindowState == FormWindowState.Minimized) { if (clearTitle) form.Text = ""; form.Hide(); } };
        form.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "窗口行为验证\n本测试窗口会自动退出。", TextAlign = System.Drawing.ContentAlignment.MiddleCenter });
        var timer = new Timer { Interval = 100 };
        timer.Tick += delegate
        {
            if (nativeTrayAt >= 0 && ownTray == null && !trayRemoved && (DateTime.UtcNow - start).TotalMilliseconds >= nativeTrayAt)
            { ownTray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = name + " original", Visible = true }; ownTray.MouseClick += delegate { ShowWindow(form.Handle, 9); }; }
            if (nativeTrayUntil >= 0 && !trayRemoved && ownTray != null && (DateTime.UtcNow - start).TotalMilliseconds >= nativeTrayUntil)
            { ownTray.Visible = false; ownTray.Dispose(); ownTray = null; trayRemoved = true; }
            if (hangAfter > 0 && !didHang && (DateTime.UtcNow - start).TotalMilliseconds >= hangAfter)
            {
                didHang = true; File.AppendAllText(file, "HANG|" + DateTime.UtcNow.ToString("o") + "\n", Encoding.UTF8);
                System.Threading.Thread.Sleep(laterHang);
            }
            if (selfShow > 0 && !shownAgain && (DateTime.UtcNow - start).TotalMilliseconds >= selfShow)
            { shownAgain = true; ShowWindow(form.Handle, 9); }
            string state = IsIconic(form.Handle) ? "Minimized" : IsZoomed(form.Handle) ? "Maximized" : "Normal";
            File.AppendAllText(file, "STATE|" + DateTime.UtcNow.ToString("o") + "|" + IsWindowVisible(form.Handle) + "|" + state + "\n", Encoding.UTF8);
            if ((DateTime.UtcNow - start).TotalMilliseconds >= lifetime) { timer.Stop(); form.Close(); }
        };
        form.Shown += delegate
        {
            File.AppendAllText(file, "HANDLE|" + form.Handle.ToInt64() + "\n", Encoding.UTF8);
            timer.Start();
            if (startMin) form.WindowState = FormWindowState.Minimized;
            if (hang > 0) form.BeginInvoke(new Action(delegate { System.Threading.Thread.Sleep(hang); }));
        };
        Application.Run(form); timer.Dispose(); if (ownTray != null) { ownTray.Visible = false; ownTray.Dispose(); }
        File.AppendAllText(file, "END|" + DateTime.UtcNow.ToString("o") + "\n", Encoding.UTF8);
    }
}
