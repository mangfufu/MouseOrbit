using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MouseOrbit;

internal static class Native
{
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point Pt; public uint Mouse, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    internal static bool ExcludeFromCapture(Window window) => SetWindowDisplayAffinity(new WindowInteropHelper(window).EnsureHandle(), 0x11);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    internal static void DarkTitleBar(Window window)
    { int enabled = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int)); }

    internal static void OverlayStyle(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x80 | (clickThrough ? 0x20 : 0));
    }
    internal static void Position(Window window, int x, int y)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, new nint(-1), x, y, 0, 0, 0x0011);
    }
    internal static bool SendKeys(ushort[] keys)
    {
        uint Extended(ushort key) => key is >= 0x21 and <= 0x2E or >= 0xA6 and <= 0xB7 or 0x5B or 0x5C ? 1u : 0u;
        var inputs = keys.Select(k => new Input { Type = 1, Data = new InputUnion { Key = new KeyInput { Key = k, Flags = Extended(k) } } })
            .Concat(keys.Reverse().Select(k => new Input { Type = 1, Data = new InputUnion { Key = new KeyInput { Key = k, Flags = 2 | Extended(k) } } })).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }
    internal static bool RightClick()
    {
        Input[] inputs = [new() { Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0008 } } }, new() { Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0010 } } }];
        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }
}

internal sealed class GlobalInput : IDisposable
{
    private readonly Native.HookProc _mouseProc;
    private nint _mouseHook;
    // Callbacks only update small in-memory state and enqueue rendering work.
    public Func<int, Native.MouseData, bool>? Mouse;
    public GlobalInput()
    {
        _mouseProc = (code, message, data) =>
        {
            if (code >= 0)
            {
                var value = Marshal.PtrToStructure<Native.MouseData>(data);
                if ((value.Flags & 1) == 0 && Mouse?.Invoke((int)message, value) == true) return 1;
            }
            return Native.CallNextHookEx(_mouseHook, code, message, data);
        };
        _mouseHook = Native.SetWindowsHookEx(14, _mouseProc, Native.GetModuleHandle(null), 0);
        if (_mouseHook == 0) { Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法注册全局鼠标输入"); }
    }
    public void Dispose()
    {
        if (_mouseHook != 0) Native.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }
}
