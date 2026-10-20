using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseOrbit;

internal static class DesktopActions
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement
    {
        public uint Length, Flags, Show;
        public Native.Point Minimum, Maximum;
        public Rect Normal;
    }
    private sealed record SnapState(uint Process, Placement Before, Rect Snapped, string Side);
    private static readonly Dictionary<nint, SnapState> Snaps = new();
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    internal static bool Activate(nint hwnd)
    { if (!IsWindow(hwnd)) return false; if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); return Native.SetForegroundWindow(hwnd); }
    internal static bool IsTopmost(nint window) => IsWindow(window) && (Native.GetWindowLong(window, -20) & 8) != 0;
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
            case "topmost": return Native.SetWindowPos(target, IsTopmost(target) ? new nint(-2) : new nint(-1), 0, 0, 0, 0, 0x13);
        }
        var screen = System.Windows.Forms.Screen.FromHandle(target);
        var area = screen.WorkingArea;
        if (!GetWindowRect(target, out var current)) return false;
        if (command.Id is "snapLeft" or "snapRight") return ToggleSnap(target, command.Id, current, area);
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
    private static bool Near(Rect a, Rect b, int tolerance = 3) => Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance && Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;
    private static Rect Half(System.Drawing.Rectangle area, string side)
    {
        int middle = area.Left + area.Width / 2;
        return new() { Left = side == "snapLeft" ? area.Left : middle, Top = area.Top, Right = side == "snapLeft" ? middle : area.Right, Bottom = area.Bottom };
    }
    private static bool ToggleSnap(nint window, string side, Rect current, System.Drawing.Rectangle area)
    {
        WindowNative.GetWindowThreadProcessId(window, out uint process);
        var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(window, ref placement)) return false;
        if (Snaps.TryGetValue(window, out var saved) && (saved.Process != process || !Near(current, saved.Snapped) || IsZoomed(window)))
        { Snaps.Remove(window); saved = null; }
        if (saved != null && saved.Side == side)
        {
            var original = saved.Before;
            bool restored = SetWindowPlacement(window, ref original);
            if (restored) Snaps.Remove(window);
            return restored;
        }
        var destination = Half(area, side);
        if (saved == null && !IsZoomed(window) && Near(current, destination, 16))
        {
            // Windows or another tool may have snapped this window before MouseOrbit saw it.
            return Execute(Commands.Find("centerWindow"), window);
        }
        var before = saved?.Before ?? placement;
        // Restore before resizing; an asynchronous restore can overwrite the new snapped rectangle later.
        ShowWindow(window, 9);
        if (!Native.SetWindowPos(window, 0, destination.Left, destination.Top, destination.Right - destination.Left, destination.Bottom - destination.Top, 0x0044)) return false;
        if (GetWindowRect(window, out var snapped)) Snaps[window] = new(process, before, snapped, side);
        if (Snaps.Count > 128) foreach (var key in Snaps.Keys.Where(w => !IsWindow(w)).ToArray()) Snaps.Remove(key);
        return true;
    }
}
