using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseOrbit;

internal static class OpenWindows
{
    private delegate bool EnumCallback(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    internal static string Title(nint hwnd) { var text = new StringBuilder(512); GetWindowText(hwnd, text, text.Capacity); return text.ToString(); }
    internal static DesktopEntry? ForegroundApplication()
    {
        var handle = Native.GetForegroundWindow(); if (handle == 0) return null;
        GetWindowThreadProcessId(handle, out var pid); if (pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (new[] { "explorer", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LockApp", "dwm" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) return null;
            var module = process.MainModule; var path = module?.FileName;
            if (string.IsNullOrEmpty(path)) return null;
            var name = module!.FileVersionInfo.FileDescription;
            if (string.IsNullOrWhiteSpace(name)) name = process.ProcessName;
            return new("observed:" + path.ToLowerInvariant(), name, "最近使用的应用 · " + process.ProcessName, "app", path, "▦");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    internal static List<DesktopEntry> List()
    {
        var windows = new List<DesktopEntry>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || (Native.GetWindowLong(handle, -20) & 0x80) != 0) return true;
            var title = Title(handle); if (title.Length == 0 || title == "Program Manager") return true;
            GetWindowThreadProcessId(handle, out var pid); if (pid == Environment.ProcessId) return true;
            string name;
            try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName; }
            catch (ArgumentException) { return true; }
            windows.Add(new("window:" + handle, title, "正在运行 · " + name, "window", handle.ToString(), "▣"));
            return true;
        }, 0);
        return windows;
    }
}
