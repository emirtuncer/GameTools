using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTools.Core;

public static class WindowHelper
{
    public static List<WindowInfo> GetWindows()
    {
        var list = new List<WindowInfo>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd)) return true;
            int len = Win32.GetWindowTextLength(hwnd);
            if (len == 0) return true;
            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            if ((exStyle & Win32.WS_EX_TOOLWINDOW) != 0 && (exStyle & Win32.WS_EX_APPWINDOW) == 0) return true;
            if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return true;

            var buf = new char[len + 1];
            Win32.GetWindowText(hwnd, buf, len + 1);
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            Win32.GetWindowRect(hwnd, out Win32.RECT r);

            string proc = "unknown";
            try { proc = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            list.Add(new WindowInfo(hwnd, new string(buf, 0, len), proc, pid,
                r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static WindowInfo GetInfo(IntPtr hwnd)
    {
        int len = Win32.GetWindowTextLength(hwnd);
        var buf = new char[len + 1];
        Win32.GetWindowText(hwnd, buf, buf.Length);
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        Win32.GetWindowRect(hwnd, out Win32.RECT r);
        string proc = "unknown";
        try { proc = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return new WindowInfo(hwnd, new string(buf, 0, Math.Max(len, 0)), proc, pid,
            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static Win32.MONITORINFO GetMonitor(IntPtr hwnd)
    {
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref mi);
        return mi;
    }

    public static void Center(IntPtr hwnd, int? tw = null, int? th = null)
    {
        var mi = GetMonitor(hwnd);
        var work = mi.rcWork;
        int mw = work.Right - work.Left, mh = work.Bottom - work.Top;
        if (!tw.HasValue || !th.HasValue)
        {
            Win32.GetWindowRect(hwnd, out Win32.RECT r);
            tw ??= r.Right - r.Left;
            th ??= r.Bottom - r.Top;
        }
        int x = work.Left + (mw - tw.Value) / 2;
        int y = work.Top + (mh - th.Value) / 2;
        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, y, tw.Value, th.Value, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public static Win32.RECT ClipToWindow(IntPtr hwnd, int edgePad = Constants.EdgePadding)
    {
        var pt = new Win32.POINT();
        Win32.ClientToScreen(hwnd, ref pt);
        Win32.GetClientRect(hwnd, out Win32.RECT client);
        var clip = new Win32.RECT { Left = pt.X, Top = pt.Y, Right = pt.X + client.Right, Bottom = pt.Y + client.Bottom };

        var mi = GetMonitor(hwnd);
        var mon = mi.rcMonitor;
        if (clip.Left <= mon.Left) clip.Left = mon.Left + edgePad;
        if (clip.Top <= mon.Top) clip.Top = mon.Top + edgePad;
        if (clip.Right >= mon.Right) clip.Right = mon.Right - edgePad;
        if (clip.Bottom >= mon.Bottom) clip.Bottom = mon.Bottom - edgePad;

        Win32.ClipCursor(ref clip);
        return clip;
    }

    public static void RemoveBorder(IntPtr hwnd)
    {
        int style = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
        style &= ~(Win32.WS_CAPTION | Win32.WS_THICKFRAME);
        Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, style);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
    }
}
