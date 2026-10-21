using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

// Small reusable premultiplied DIBs avoid creating a WPF render target for a screen-sized overlay.
internal sealed class LayeredSurface : System.Windows.Forms.NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize;
        public int XPels, YPels; public uint Colors, Important;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend
    { public byte Operation, Flags, Alpha, Format; }
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint destination, ref Native.Point point, ref Size size, nint source, ref Native.Point origin, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    private readonly nint _dc;
    private nint _bitmapHandle, _previous, _bits;
    private System.Drawing.Bitmap? _bitmap;
    private System.Drawing.Graphics? _graphics;
    private int _width, _height;
    private System.Windows.Rect _bounds = System.Windows.Rect.Empty;
    internal bool Visible { get; private set; }
    internal int PixelArea => _width * _height;
    public LayeredSurface(string title)
    {
        CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = title, ClassName = "STATIC", Width = 1, Height = 1,
            Style = unchecked((int)0x80000000), ExStyle = 0x080800A8 // popup, layered, click-through, topmost, no activation
        });
        int disableTransitions = 1;
        DwmSetWindowAttribute(Handle, 3, ref disableTransitions, sizeof(int));
        _dc = CreateCompatibleDC(0);
        if (_dc == 0) { DestroyHandle(); throw new System.ComponentModel.Win32Exception(); }
    }
    internal void Paint(System.Windows.Rect bounds, Action<System.Drawing.Graphics> draw, bool show = true)
    {
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width)), height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        if (width != _width || height != _height)
        {
            ReleaseBitmap();
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, Bits = 32 };
            _bitmapHandle = CreateDIBSection(_dc, ref info, 0, out _bits, 0, 0);
            if (_bitmapHandle == 0) throw new System.ComponentModel.Win32Exception();
            _previous = SelectObject(_dc, _bitmapHandle); _width = width; _height = height;
            _bitmap = new System.Drawing.Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _bits);
            _graphics = System.Drawing.Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        }
        var graphics = _graphics!;
        graphics.ResetTransform();
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; graphics.Clear(System.Drawing.Color.Transparent);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver; draw(graphics); graphics.Flush();
        var point = new Native.Point { X = (int)bounds.X, Y = (int)bounds.Y }; var size = new Size { Width = width, Height = height }; var origin = new Native.Point();
        var blend = new Blend { Alpha = 255, Format = 1 };
        if (!UpdateLayeredWindow(Handle, 0, ref point, ref size, _dc, ref origin, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        _bounds = bounds;
        if (show) Show();
    }
    internal void Move(int x, int y)
    {
        if (!_bounds.IsEmpty && _bounds.X == x && _bounds.Y == y) return;
        Native.SetWindowPos(Handle, 0, x, y, 0, 0, 0x0215);
        _bounds = new System.Windows.Rect(x, y, _width, _height);
    }
    internal void Show()
    {
        if (Visible) return;
        // A pre-created topmost HWND can sit below windows pinned after startup.
        // Raise once at the beginning of each gesture, never on every mouse move.
        Native.SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, 0x0253); // NOSIZE | NOMOVE | NOACTIVATE | SHOWWINDOW | NOOWNERZORDER
        Visible = true;
    }
    internal BitmapSource Snapshot()
    {
        var image = BitmapSource.Create(_width, _height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, _bits, _width * _height * 4, _width * 4);
        image.Freeze(); return image;
    }
    internal void Hide() { if (Visible) ShowWindow(Handle, 0); Visible = false; }
    internal void ReleaseIdleBuffer()
    {
        Hide();
        // Keep the tiny preheated DIB; a long, cross-screen gesture must not
        // leave a screen-sized drawing buffer resident after the stroke ends.
        if (PixelArea > 128 * 128) ReleaseBitmap();
    }
    private void ReleaseBitmap()
    {
        _graphics?.Dispose(); _graphics = null; _bitmap?.Dispose(); _bitmap = null;
        if (_bitmapHandle != 0) { SelectObject(_dc, _previous); DeleteObject(_bitmapHandle); _bitmapHandle = 0; }
        _width = _height = 0;
    }
    public void Dispose() { Hide(); ReleaseBitmap(); DeleteDC(_dc); DestroyHandle(); }
}
