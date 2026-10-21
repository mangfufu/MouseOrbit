using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

// A cached material, not a simulation: fine grain, diffuse matter and a few closer particles.
internal static class ParticleTexture
{
    internal enum Form { Rest, Gather, Wing, Joined }
    private readonly record struct Key(Form Form, int Variant, int Width, int Height, bool Dark);
    private static readonly Dictionary<Key, Drawing> Cache = new();
    private static readonly Queue<Key> Order = new();
    internal static Drawing Get(Form form, int seed, double width, double height)
    {
        var key = new Key(form, seed & 3, (int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), Palette.Dark);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var drawing = Build(key, width, height);
        if (Cache.Count >= 64) Cache.Remove(Order.Dequeue());
        Cache.Add(key, drawing); Order.Enqueue(key); return drawing;
    }
    private static double RoundBox(double x, double y, double left, double top, double width, double height, double radius)
    {
        double qx = Math.Abs(x - left - width / 2) - width / 2 + radius;
        double qy = Math.Abs(y - top - height / 2) - height / 2 + radius;
        return Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }
    private static double Join(double a, double b)
    { double h = Math.Max(12 - Math.Abs(a - b), 0) / 12; return Math.Min(a, b) - h * h * 3; }
    private static Drawing Build(Key key, double width, double height)
    {
        var pixels = new byte[key.Width * key.Height * 4];
        var random = new Random(271 + key.Variant * 149 + (int)key.Form * 37);
        var ink = Palette.Ink.Color; var paper = Palette.Surface.Color;
        double mix = Palette.Dark ? .25 : .18;
        byte Tone(byte a, byte b) => (byte)Math.Round(a * (1 - mix) + b * mix);
        var pigment = Color.FromRgb(Tone(ink.R, paper.R), Tone(ink.G, paper.G), Tone(ink.B, paper.B));
        for (int row = 0; row < key.Height; row++) for (int column = 0; column < key.Width; column++)
        {
            double x = (column + .5) * width / key.Width, y = (row + .5) * height / key.Height;
            double density, fog;
            if (key.Form == Form.Rest)
            {
                double radius = Math.Sqrt(Math.Pow(x - 25, 2) + Math.Pow(y - height / 2, 2));
                double crescent = Math.Exp(-Math.Pow((radius - 20) / 3.4, 2)) * Math.Clamp((33 - x) / 16, 0, 1);
                double trailY = height / 2 + 19 - .045 * x + 2 * Math.Sin(x / 37 + key.Variant);
                double trail = Math.Exp(-Math.Pow((y - trailY) / 2.5, 2)) * Math.Exp(-x / 95);
                density = crescent * .49 + trail * .075; fog = crescent * .018;
            }
            else
            {
                double distance;
                if (key.Form == Form.Wing)
                {
                    double body = RoundBox(x, y, 12, 9, width - 21, height - 18, 24);
                    double neck = RoundBox(x, y, -9, 30, 40, 34, 13);
                    distance = Join(body, neck);
                }
                else if (key.Form == Form.Joined)
                {
                    double t = Math.Clamp(x / width, 0, 1);
                    double close = Math.Pow(Math.Clamp((t - .56) / .44, 0, 1), 1.6);
                    double upper = height / 2 - 13 - 4 * Math.Sin(t * Math.PI) + close * 10;
                    double lower = height / 2 + 13 + 3 * Math.Sin(t * Math.PI) - close * 10;
                    distance = Math.Min(Math.Abs(y - upper), Math.Abs(y - lower));
                    distance += Math.Max(34 - x, 0) * .3;
                }
                else
                {
                    double bend = Math.Sin(Math.Clamp(x / width, 0, 1) * Math.PI) * 2;
                    distance = RoundBox(x, y - bend, 5, 8, width - 10, height - 16, (height - 16) / 2);
                }
                double edge = Math.Exp(-Math.Pow(distance / 1.65, 2));
                double diffuse = Math.Exp(-Math.Pow(distance / 6, 2));
                double variation = .72 + .28 * Math.Sin(x / 19 + y / 11 + key.Variant);
                density = (edge * .72 + diffuse * .13 + (distance < 0 ? .014 : 0)) * variation;
                if (key.Form == Form.Wing) density *= .44 + .28 * Math.Sin(y / 37 + x / 31) * Math.Sin(y / 37 + x / 31);
                fog = diffuse * .035 + (distance < 0 ? .012 : 0);
            }
            double noise = random.NextDouble();
            double alpha = fog + (noise < density ? .18 + Math.Pow(random.NextDouble(), 2) * .66 : 0);
            if (alpha < .002) continue;
            int offset = (row * key.Width + column) * 4; byte a = (byte)Math.Clamp((int)(alpha * 255), 0, 220);
            pixels[offset] = (byte)(pigment.B * a / 255); pixels[offset + 1] = (byte)(pigment.G * a / 255);
            pixels[offset + 2] = (byte)(pigment.R * a / 255); pixels[offset + 3] = a;
        }
        var bitmap = BitmapSource.Create(key.Width, key.Height, 192, 192, PixelFormats.Pbgra32, null, pixels, key.Width * 4); bitmap.Freeze();
        var drawing = new ImageDrawing(bitmap, new Rect(0, 0, width, height)); drawing.Freeze(); return drawing;
    }
}
