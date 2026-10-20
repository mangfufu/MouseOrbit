using System.Windows.Media;

namespace MouseOrbit;

internal readonly record struct OverlayColors(Color Surface, Color Ink, Color Muted, Color Edge, Color Accent, string Theme)
{
    internal static OverlayColors Capture() => new(Palette.Surface.Color, Palette.Ink.Color, Palette.Muted.Color, Palette.Edge.Color, Palette.Accent.Color, Palette.Current.Id);
}

internal sealed class GestureHint : IDisposable
{
    private readonly LayeredSurface _surface = new("MouseOrbit · 手势提示");
    private readonly WindowOutline _outline = new();
    private readonly System.Drawing.Font _nameFont = new("Microsoft YaHei UI", 14, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
    private readonly System.Drawing.Font _titleFont = new("Microsoft YaHei UI", 11, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
    private nint _titleWindow;
    private long _nextTitle;
    private string _title = "", _paintedName = "", _paintedTitle = "", _paintedTheme = "";
    private double _paintedScale;
    internal nint Handle => _surface.Handle;
    internal System.Windows.Media.Imaging.BitmapSource Snapshot() => _surface.Snapshot();
    private static System.Drawing.Color ColorOf(Color color) => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    public void Prepare(OverlayColors? colors = null) => Paint("继续划动", "MouseOrbit · 窗口", 1, 0, 0, colors ?? OverlayColors.Capture(), false);
    private void Paint(string name, string title, double scale, int x, int y, OverlayColors colors, bool show = true)
    {
        _surface.Paint(new Rect(x, y, Math.Ceiling(270 * scale), Math.Ceiling(67 * scale)), graphics =>
        {
            graphics.ScaleTransform((float)scale, (float)scale);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var background = new System.Drawing.SolidBrush(ColorOf(colors.Surface));
            using var ink = new System.Drawing.SolidBrush(ColorOf(colors.Ink));
            using var muted = new System.Drawing.SolidBrush(ColorOf(colors.Muted));
            using var edge = new System.Drawing.Pen(ColorOf(colors.Edge), 1);
            using var format = new System.Drawing.StringFormat { Trimming = System.Drawing.StringTrimming.EllipsisCharacter, FormatFlags = System.Drawing.StringFormatFlags.NoWrap };
            graphics.FillRectangle(background, 0, 0, 270, 67); graphics.DrawRectangle(edge, .5f, .5f, 269, 66);
            graphics.DrawString(name, _nameFont, ink, new System.Drawing.RectangleF(12, 9, 246, 22), format);
            graphics.DrawString(title, _titleFont, muted, new System.Drawing.RectangleF(12, 34, 246, 20), format);
        }, show);
        _paintedName = name; _paintedTitle = title; _paintedTheme = colors.Theme; _paintedScale = scale;
    }
    public void Update(Native.Point point, WindowTarget target, string name, bool global = false, OverlayColors? palette = null)
    {
        var colors = palette ?? OverlayColors.Capture();
        if (!global && (_titleWindow != target.Handle || Environment.TickCount64 >= _nextTitle))
        { _titleWindow = target.Handle; _nextTitle = Environment.TickCount64 + 1000; _title = OpenWindows.Title(target.Handle); }
        string title = global ? "" : _title;
        double scale = Math.Max(1, Native.GetDpiForWindow(Handle) / 96d);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        int x = Math.Clamp(point.X + 22, area.Left, Math.Max(area.Left, area.Right - (int)Math.Ceiling(270 * scale)));
        int y = Math.Clamp(point.Y + 20, area.Top, Math.Max(area.Top, area.Bottom - (int)Math.Ceiling(67 * scale)));
        _surface.Move(x, y);
        double movedScale = Math.Max(1, Native.GetDpiForWindow(Handle) / 96d);
        if (movedScale != scale)
        {
            scale = movedScale;
            x = Math.Clamp(point.X + 22, area.Left, Math.Max(area.Left, area.Right - (int)Math.Ceiling(270 * scale)));
            y = Math.Clamp(point.Y + 20, area.Top, Math.Max(area.Top, area.Bottom - (int)Math.Ceiling(67 * scale)));
            _surface.Move(x, y);
        }
        if (_paintedName != name || _paintedTitle != title || _paintedTheme != colors.Theme || _paintedScale != scale) Paint(name, title, scale, x, y, colors);
        else _surface.Show();
        if (global || !target.Valid || !WindowNative.GetWindowRect(target.Handle, out var box)) { _outline.Hide(); return; }
        _outline.Show(box, colors.Accent);
    }
    public void Clear() { _surface.Hide(); _outline.Hide(); }
    public void Dispose() { _outline.Dispose(); _surface.Dispose(); _nameFont.Dispose(); _titleFont.Dispose(); }
}
