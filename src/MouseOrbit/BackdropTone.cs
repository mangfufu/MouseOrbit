using System.Windows.Media;

namespace MouseOrbit;

// A small, transient sample is taken before the overlay appears; no desktop image is retained.
internal static class BackdropTone
{
    internal static bool IsDark(IEnumerable<Color> samples)
    {
        var luminance = samples.Select(c => .2126 * c.R + .7152 * c.G + .0722 * c.B).Order().ToArray();
        return luminance.Length > 0 && luminance[luminance.Length / 2] < 128;
    }
    internal static void Adapt(Native.Point point)
    {
        try
        {
            var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).Bounds;
            int size = Math.Min(96, Math.Min(area.Width, area.Height));
            int x = Math.Clamp(point.X - size / 2, area.Left, area.Right - size);
            int y = Math.Clamp(point.Y - size / 2, area.Top, area.Bottom - size);
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(size, size), System.Drawing.CopyPixelOperation.SourceCopy);
            var samples = new List<Color>(36);
            for (int row = 0; row < 6; row++) for (int column = 0; column < 6; column++)
            {
                var pixel = bitmap.GetPixel((2 * column + 1) * size / 12, (2 * row + 1) * size / 12);
                samples.Add(Color.FromRgb(pixel.R, pixel.G, pixel.B));
            }
            Palette.SetBackdropDark(IsDark(samples));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or ArgumentException)
        { /* Keep the last readable material when the desktop cannot be sampled. */ }
    }
}
