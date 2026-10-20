using System.Runtime.InteropServices;
using System.Windows.Media;

namespace MouseOrbit;

// One hollow native region replaces four synchronous layered-bitmap uploads.
internal sealed class WindowOutline : System.Windows.Forms.NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct PaintInfo
    {
        public nint Dc; public int Erase; public WindowNative.Box Rect; public int Restore, Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintInfo paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref PaintInfo paint);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref WindowNative.Box rect, nint brush);
    private nint _brush;
    private WindowNative.Box _bounds;
    private Color _color;
    private bool _visible;
    internal WindowOutline()
    {
        CreateHandle(new System.Windows.Forms.CreateParams { Caption = "MouseOrbit · 目标边框", ClassName = "STATIC",
            Width = 1, Height = 1, Style = unchecked((int)0x80000000), ExStyle = 0x080800A8 });
        SetLayeredWindowAttributes(Handle, 0, 255, 2);
    }
    internal void Show(WindowNative.Box bounds, Color color)
    {
        if (bounds.Width < 1 || bounds.Height < 1) { Hide(); return; }
        if (_visible && _bounds.Equals(bounds) && _color == color) return;
        bool resized = bounds.Width != _bounds.Width || bounds.Height != _bounds.Height;
        if (_brush == 0 || _color != color)
        {
            if (_brush != 0) DeleteObject(_brush);
            _brush = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16)); _color = color;
        }
        if (resized)
        {
            var outer = CreateRectRgn(0, 0, bounds.Width, bounds.Height);
            var inner = CreateRectRgn(2, 2, Math.Max(2, bounds.Width - 2), Math.Max(2, bounds.Height - 2));
            try
            {
                CombineRgn(outer, outer, inner, 4);
                if (SetWindowRgn(Handle, outer, true) != 0) outer = 0; // The window now owns the region.
            }
            finally { if (outer != 0) DeleteObject(outer); DeleteObject(inner); }
        }
        _bounds = bounds;
        Native.SetWindowPos(Handle, _visible ? 0 : new nint(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, _visible ? 0x0214u : 0x0250u);
        _visible = true; InvalidateRect(Handle, 0, false); UpdateWindow(Handle);
    }
    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == 0x14) { message.Result = 1; return; }
        if (message.Msg == 0xF)
        {
            var dc = BeginPaint(Handle, out var paint);
            try { if (_brush != 0) FillRect(dc, ref paint.Rect, _brush); }
            finally { EndPaint(Handle, ref paint); }
            message.Result = 0; return;
        }
        base.WndProc(ref message);
    }
    internal void Hide() { if (_visible) ShowWindow(Handle, 0); _visible = false; }
    public void Dispose() { Hide(); DestroyHandle(); if (_brush != 0) { DeleteObject(_brush); _brush = 0; } }
}
