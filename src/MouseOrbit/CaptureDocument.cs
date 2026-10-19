using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

internal sealed record Markup(string Kind, Point Start, Point End, Color Color, double Width, double FontSize = 24, string Text = "", IReadOnlyList<Point>? Points = null);

internal sealed class CaptureDocument
{
    public BitmapSource Image { get; }
    public List<Markup> Marks { get; } = new();
    private readonly Stack<Markup> _redo = new();
    public CaptureDocument(BitmapSource image) { Image = image; }
    public void Add(Markup mark) { Marks.Add(mark); _redo.Clear(); }
    public void Undo() { if (Marks.Count > 0) { _redo.Push(Marks[^1]); Marks.RemoveAt(Marks.Count - 1); } }
    public void Redo() { if (_redo.Count > 0) Marks.Add(_redo.Pop()); }
    public void DrawMarks(DrawingContext dc, Markup? current = null)
    {
        foreach (var mark in Marks) Draw(dc, mark);
        if (current != null) Draw(dc, current);
    }
    private static void Draw(DrawingContext dc, Markup m)
    {
        var brush = new SolidColorBrush(m.Color);
        var pen = new Pen(brush, m.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var rect = new Rect(m.Start, m.End);
        switch (m.Kind)
        {
            case "rectangle": dc.DrawRectangle(null, pen, rect); break;
            case "ellipse": dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2); break;
            case "text":
                dc.DrawText(new FormattedText(m.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), m.FontSize, brush, 1) { MaxTextWidth = Math.Max(1, 2400 - m.Start.X) }, m.Start); break;
            case "pen":
                var points = m.Points ?? new[] { m.Start, m.End };
                for (int i = 1; i < points.Count; i++) dc.DrawLine(pen, points[i - 1], points[i]); break;
            case "arrow":
                dc.DrawLine(pen, m.Start, m.End);
                var vector = m.End - m.Start;
                if (vector.Length > 1)
                {
                    vector.Normalize(); double head = Math.Max(10, m.Width * 4);
                    var normal = new Vector(-vector.Y, vector.X);
                    dc.DrawLine(pen, m.End, m.End - vector * head + normal * head * .45);
                    dc.DrawLine(pen, m.End, m.End - vector * head - normal * head * .45);
                }
                break;
        }
    }
    public BitmapSource Render()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Image.PixelWidth, Image.PixelHeight)));
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight)); DrawMarks(dc); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Image.PixelWidth, Image.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}

internal static class ScreenPixels
{
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    internal static BitmapSource Read(System.Drawing.Rectangle area, int maximumEdge = int.MaxValue)
    {
        using var bitmap = new System.Drawing.Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        if (Math.Max(area.Width, area.Height) > maximumEdge)
        {
            double scale = maximumEdge / (double)Math.Max(area.Width, area.Height);
            using var small = new System.Drawing.Bitmap(Math.Max(1, (int)(area.Width * scale)), Math.Max(1, (int)(area.Height * scale)));
            using (var g = System.Drawing.Graphics.FromImage(small)) g.DrawImage(bitmap, 0, 0, small.Width, small.Height);
            return Convert(small);
        }
        return Convert(bitmap);
    }
    private static BitmapSource Convert(System.Drawing.Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try { var result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); result.Freeze(); return result; }
        finally { DeleteObject(handle); }
    }
    internal static void SavePng(BitmapSource image, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream); }
}
