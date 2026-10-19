namespace MouseOrbit;

internal readonly record struct StrokePoint(double X, double Y);

// All coordinates are device-independent. Discrete commands wait for the whole stroke.
// Continuous volume starts only after a clear horizontal leg and vertical turn.
internal sealed class StrokeGesture
{
    private readonly List<StrokePoint> _points = new();
    private readonly double _minimum;
    public IReadOnlyList<StrokePoint> Points => _points;
    public bool HasMovement => _points.Count > 1 && Length(_points) >= _minimum;
    public bool VolumeMode { get; private set; }
    private double _volumeY;
    public StrokeGesture(double x, double y, double minimum = 30, long time = 0)
    { _points.Add(new(x, y)); _minimum = minimum; }
    public void Add(double x, double y, long time)
    {
        var p = new StrokePoint(x, y);
        if (Distance(_points[^1], p) < 2) return;
        if (_points.Count >= 2048)
        {
            var reduced = _points.Where((_, i) => i % 2 == 0).ToArray(); _points.Clear(); _points.AddRange(reduced);
        }
        _points.Add(p);
    }
    public bool TryEnterVolume()
    {
        if (VolumeMode || _points.Count < 3) return false;
        var start = _points[0]; var end = _points[^1];
        double firstLength = 0, total = Length(_points);
        for (int i = 1; i < _points.Count - 1; i++)
        {
            var corner = _points[i]; firstLength += Distance(_points[i - 1], corner);
            double right = corner.X - start.X, vertical = Math.Abs(end.Y - corner.Y);
            if (right < Math.Max(40, _minimum * 1.4) || Math.Abs(corner.Y - start.Y) > Math.Max(6, right * .10)) continue;
            if (vertical < 12 || Math.Abs(end.X - corner.X) > Math.Max(4, vertical * .24)) continue;
            if (firstLength > Distance(start, corner) * 1.10 || total - firstLength > Distance(corner, end) * 1.12) continue;
            VolumeMode = true; _volumeY = corner.Y; return true;
        }
        return false;
    }
    public int TakeVolumeSteps()
    {
        if (!VolumeMode) return 0;
        int steps = (int)((_volumeY - _points[^1].Y) / 12);
        steps = Math.Clamp(steps, -5, 5); _volumeY -= steps * 12; return steps;
    }
    public string Recognize()
    {
        if (!HasMovement || VolumeMode) return "";
        var p = Resample(_points, 64);
        double left = p.Min(a => a.X), right = p.Max(a => a.X), top = p.Min(a => a.Y), bottom = p.Max(a => a.Y);
        double w = right - left, h = bottom - top, length = Length(p), gap = Distance(p[0], p[^1]);
        double diagonal = Math.Sqrt(w * w + h * h);
        var first = p[0]; var last = p[^1];
        // Straight strokes must have a clear axis and very little detour.
        if (gap >= _minimum && length <= gap * 1.16)
        {
            double dx = last.X - first.X, dy = last.Y - first.Y;
            if (Math.Abs(dx) > Math.Abs(dy) * 2.3) return dx > 0 ? "R" : "L";
            if (Math.Abs(dy) > Math.Abs(dx) * 2.3) return dy > 0 ? "D" : "U";
        }
        if (w < _minimum || h < _minimum) return "";
        double cx = (left + right) / 2, cy = (top + bottom) / 2;
        var angles = p.Select(a => Math.Atan2((a.Y - cy) / h, (a.X - cx) / w)).ToArray();
        double sweep = 0, absolute = 0;
        for (int i = 1; i < angles.Length; i++)
        {
            double d = angles[i] - angles[i - 1];
            while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2;
            sweep += d; absolute += Math.Abs(d);
        }
        double coherence = Math.Abs(sweep) / Math.Max(.001, absolute);
        var radii = p.Select(a => Math.Sqrt(Math.Pow((a.X - cx) / w, 2) + Math.Pow((a.Y - cy) / h, 2))).ToArray();
        double radius = radii.Average();
        double variation = Math.Sqrt(radii.Average(r => (r - radius) * (r - radius))) / radius;
        // Closed loop and visibly open C have a gap between their acceptance regions.
        if (w / h is > .48 and < 2.05 && gap < diagonal * .20 && Math.Abs(sweep) is > 5.2 and < 7.1 && coherence > .87 && variation < .28) return "O";
        if (w / h is > .40 and < 1.9 && gap > h * .48 && Math.Abs(sweep) is > 3.7 and < 5.6 && coherence > .88 && variation < .28
            && first.X > left + w * .64 && last.X > left + w * .64
            && Math.Abs(last.Y - first.Y) > h * .46
            && !p.Any(a => a.X > left + w * .84 && a.Y > top + h * .35 && a.Y < top + h * .65)) return "C";
        int pivot = Array.FindIndex(p, a => a.Y == bottom);
        if (pivot is > 10 and < 54 && Math.Abs(first.Y - last.Y) < h * .30
            && p[pivot].X > left + w * .25 && p[pivot].X < left + w * .75
            && Math.Abs(last.X - first.X) > w * .80 && first.Y < top + h * .25 && last.Y < top + h * .25
            && Length(p.Take(pivot + 1).ToArray()) < Distance(first, p[pivot]) * 1.15
            && Length(p.Skip(pivot).ToArray()) < Distance(p[pivot], last) * 1.15) return "V";
        return "";
    }
    internal static double Distance(StrokePoint a, StrokePoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double Length(IReadOnlyList<StrokePoint> p)
    { double result = 0; for (int i = 1; i < p.Count; i++) result += Distance(p[i - 1], p[i]); return result; }
    private static StrokePoint[] Resample(IReadOnlyList<StrokePoint> points, int count)
    {
        double total = Length(points), step = total / (count - 1), traversed = 0; int segment = 1;
        var result = new StrokePoint[count]; result[0] = points[0]; result[^1] = points[^1];
        for (int i = 1; i < count - 1; i++)
        {
            double wanted = step * i;
            while (segment < points.Count - 1 && traversed + Distance(points[segment - 1], points[segment]) < wanted)
            { traversed += Distance(points[segment - 1], points[segment]); segment++; }
            var a = points[segment - 1]; var b = points[segment]; double ratio = (wanted - traversed) / Math.Max(.001, Distance(a, b));
            result[i] = new(a.X + (b.X - a.X) * ratio, a.Y + (b.Y - a.Y) * ratio);
        }
        return result;
    }
}
