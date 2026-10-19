using System.Windows.Media;

namespace MouseOrbit;

internal sealed record ThemePreset(string Id, string Name, bool Dark, bool Pixel, string Surface, string Paper, string Ink, string Muted, string Accent, double Radius);

internal static class ThemePresets
{
    public static readonly ThemePreset[] All =
    [
        new("glass", "半透明 · 雾白", false, false, "#DCECF1F4", "#EEF2F4", "#34434C", "#6D7D87", "#62818D", 14),
        new("graphite", "半透明 · 石墨", true, false, "#D92A3035", "#2A3035", "#DDE4E8", "#A0ADB5", "#91ACB9", 14),
        new("sage", "半透明 · 鼠尾草", false, false, "#DDEAF0E8", "#EAF0E8", "#39483F", "#738274", "#7C937E", 10),
        new("sand", "半透明 · 暖砂", false, false, "#DDEEE7DD", "#EEE7DD", "#534A3D", "#8B7F6C", "#A68C70", 8),
        new("pixel-light", "像素 · 米白", false, true, "#E6EFE9DB", "#EFE9DB", "#423B2F", "#847865", "#AA4D2A", 1),
        new("pixel-dark", "像素 · 深灰", true, true, "#E21F1E19", "#1F1E19", "#DAD4C1", "#97907C", "#D48052", 1)
    ];
    public static ThemePreset Find(string? id, bool darkFallback = false) => All.FirstOrDefault(t => t.Id == id) ?? All[darkFallback ? 5 : 4];
}
