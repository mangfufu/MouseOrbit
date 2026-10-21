using MouseOrbit;
using System.Windows;

internal static class MemoryChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        using var surface = new LayeredSurface("Memory fixture (never shown)");
        surface.Paint(new Rect(0, 0, 128, 128), _ => { }, show: false);
        surface.ReleaseIdleBuffer();
        Check(surface.PixelArea == 128 * 128 && !surface.Visible, "clearing a short gesture retains only its tiny preheated hidden buffer");
        surface.Paint(new Rect(0, 0, 1024, 768), _ => { }, show: false);
        Check(surface.PixelArea == 1024 * 768, "long gestures can allocate the required native surface");
        surface.ReleaseIdleBuffer();
        Check(surface.PixelArea == 0 && !surface.Visible, "ending a large gesture releases its native DIB rather than retaining screen-sized memory");
        surface.ReleaseIdleBuffer();
        Check(surface.PixelArea == 0, "repeated idle cleanup is safe");
        surface.Paint(new Rect(0, 0, 128, 128), graphics => graphics.Clear(System.Drawing.Color.Red), show: false);
        Check(surface.Snapshot().PixelWidth == 128 && !surface.Visible, "the next gesture can repaint after release without showing a fixture");
        Console.WriteLine($"PASS: {count} memory checks (bounded native scratch buffers; no visible windows or input)");
    }
}
