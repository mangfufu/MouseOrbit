using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseOrbit;

internal static class DesktopActions
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    internal static bool Activate(nint hwnd)
    { if (!IsWindow(hwnd)) return false; if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); return Native.SetForegroundWindow(hwnd); }
    internal static bool Execute(Command command, nint target)
    {
        if (command.Kind == CommandKind.Launch)
        { Process.Start(new ProcessStartInfo(command.Argument!) { UseShellExecute = true }); return true; }
        if (command.Kind == CommandKind.Keys) return Native.SendKeys(command.Keys);
        if (command.Kind != CommandKind.Window) return false;
        if (!IsWindow(target)) return false;
        switch (command.Id)
        {
            case "maximize": return ShowWindowAsync(target, IsZoomed(target) ? 9 : 3);
            case "minimize": return ShowWindowAsync(target, 6);
            case "restore": return ShowWindowAsync(target, 9);
            case "closeWindow": return PostMessage(target, 0x112, 0xF060, 0);
            case "topmost": return Native.SetWindowPos(target, (Native.GetWindowLong(target, -20) & 8) != 0 ? new nint(-2) : new nint(-1), 0, 0, 0, 0, 0x13);
        }
        var screen = System.Windows.Forms.Screen.FromHandle(target);
        var area = screen.WorkingArea;
        GetWindowRect(target, out var current);
        int width = area.Width / 2, height = area.Height, x = area.Left, y = area.Top;
        if (command.Id == "snapRight") x += area.Width / 2;
        if (command.Id == "centerWindow")
        { width = (int)(area.Width * 0.75); height = (int)(area.Height * 0.8); x += (area.Width - width) / 2; y += (area.Height - height) / 2; }
        if (command.Id == "nextMonitor")
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var index = Array.FindIndex(screens, s => s.DeviceName == screen.DeviceName);
            if (screens.Length < 2) return false;
            var next = screens[(index + 1) % screens.Length].WorkingArea;
            width = Math.Min(current.Right - current.Left, next.Width); height = Math.Min(current.Bottom - current.Top, next.Height);
            x = next.Left + Math.Clamp(current.Left - area.Left, 0, next.Width - width);
            y = next.Top + Math.Clamp(current.Top - area.Top, 0, next.Height - height);
        }
        ShowWindowAsync(target, 9);
        return Native.SetWindowPos(target, 0, x, y, width, height, 0x0044);
    }
}
