using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MouseOrbit;

// DWM shares the existing window surface. Do not send PrintWindow/WM_PRINT to
// another application's UI thread just because the user opens our panel.
internal sealed class LiveThumbnail : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Properties
    {
        public uint Flags; public WindowNative.Box Destination, Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PixelSize { public int Width, Height; }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out PixelSize size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);
    private readonly Window _owner;
    private readonly FrameworkElement _host;
    private readonly ScrollViewer _viewport;
    private readonly SwitchWindow _source;
    private nint _thumbnail;
    private bool _visible, _disposed;
    private WindowNative.Box _destination, _crop;
    private WindowNative.Box _sourceBounds;
    private PixelSize _sourceSize;
    internal LiveThumbnail(Window owner, FrameworkElement host, ScrollViewer viewport, SwitchWindow source)
    {
        _owner = owner; _host = host; _viewport = viewport; _source = source;
        _host.LayoutUpdated += Updated; _host.IsVisibleChanged += VisibilityChanged; _owner.IsVisibleChanged += VisibilityChanged;
        if (owner is QuickPanelWindow quick) quick.OpenStateChanged += OpenStateChanged;
    }
    private void OpenStateChanged() => Update();
    private void Updated(object? sender, EventArgs args) => Update();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();
    private void Hide()
    {
        if (!_visible || _thumbnail == 0) return;
        var properties = new Properties { Flags = 8, Visible = false };
        DwmUpdateThumbnailProperties(_thumbnail, ref properties); _visible = false;
    }
    private static WindowNative.Box Box(Rect rectangle) => new()
    { Left = (int)Math.Round(rectangle.Left), Top = (int)Math.Round(rectangle.Top), Right = (int)Math.Round(rectangle.Right), Bottom = (int)Math.Round(rectangle.Bottom) };
    private void Update()
    {
        if (_disposed) return;
        if (!_host.IsVisible || !_owner.IsVisible || (_owner is QuickPanelWindow quick && !quick.IsVisible) || !_viewport.IsVisible || _host.ActualWidth < 1 || _host.ActualHeight < 1 || PresentationSource.FromVisual(_host) == null) { Hide(); return; }
        if (!WindowNative.IsWindow(_source.Handle) || WindowNative.GetWindowThreadProcessId(_source.Handle, out uint pid) == 0 || pid != _source.Process) { Hide(); return; }
        var dpi = VisualTreeHelper.GetDpi(_host);
        var origin = _host.PointToScreen(new()); var viewportOrigin = _viewport.PointToScreen(new());
        var full = new Rect(origin.X, origin.Y, _host.ActualWidth * dpi.DpiScaleX, _host.ActualHeight * dpi.DpiScaleY);
        var clipped = Rect.Intersect(full, new Rect(viewportOrigin.X, viewportOrigin.Y, _viewport.ViewportWidth * dpi.DpiScaleX, _viewport.ViewportHeight * dpi.DpiScaleY));
        if (clipped.IsEmpty || clipped.Width < 1 || clipped.Height < 1) { Hide(); return; }
        if (_thumbnail == 0)
        {
            var owner = new WindowInteropHelper(_owner).Handle;
            if (owner == 0 || DwmRegisterThumbnail(owner, _source.Handle, out _thumbnail) != 0) return;
        }
        if (_sourceSize.Width < 1 || (WindowNative.GetWindowRect(_source.Handle, out var sourceBounds) &&
            (sourceBounds.Width != _sourceBounds.Width || sourceBounds.Height != _sourceBounds.Height)))
        {
            if (DwmQueryThumbnailSourceSize(_thumbnail, out _sourceSize) != 0) { Hide(); return; }
            WindowNative.GetWindowRect(_source.Handle, out _sourceBounds);
        }
        var sourceSize = _sourceSize;
        if (sourceSize.Width < 1 || sourceSize.Height < 1) { Hide(); return; }
        double scale = Math.Max(full.Width / sourceSize.Width, full.Height / sourceSize.Height);
        double sx = (sourceSize.Width - full.Width / scale) / 2, sy = (sourceSize.Height - full.Height / scale) / 2;
        var crop = Box(new Rect(sx + (clipped.Left - full.Left) / scale, sy + (clipped.Top - full.Top) / scale, clipped.Width / scale, clipped.Height / scale));
        var ownerOrigin = _owner.PointToScreen(new()); clipped.Offset(-ownerOrigin.X, -ownerOrigin.Y); var destination = Box(clipped);
        if (_visible && _destination.Equals(destination) && _crop.Equals(crop)) return;
        var properties = new Properties { Flags = 0x1F, Destination = destination, Source = crop, Opacity = 255, Visible = true };
        if (DwmUpdateThumbnailProperties(_thumbnail, ref properties) == 0) { _destination = destination; _crop = crop; _visible = true; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _host.LayoutUpdated -= Updated; _host.IsVisibleChanged -= VisibilityChanged; _owner.IsVisibleChanged -= VisibilityChanged;
        if (_owner is QuickPanelWindow quick) quick.OpenStateChanged -= OpenStateChanged;
        if (_thumbnail != 0) { DwmUnregisterThumbnail(_thumbnail); _thumbnail = 0; } _visible = false;
    }
}
