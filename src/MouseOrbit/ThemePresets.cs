namespace MouseOrbit;

internal enum AppearanceLayout { Classic, Typefield }
internal sealed record ThemePreset(string Id, string Name, bool Dark, bool Pixel, string Surface, string Paper, string Ink, string Muted, string Accent, double Radius, AppearanceLayout Layout = AppearanceLayout.Classic);

internal static class ThemePresets
{
    public static readonly ThemePreset[] All =
    [
        new("typefield", "字场", false, false, "#DCEFF0F1", "#F2F3F4", "#303638", "#4B5154", "#858F94", 6, AppearanceLayout.Typefield)
    ];
    public static ThemePreset Find(string? id, bool darkFallback = false) => All.FirstOrDefault(t => t.Id == id)
        ?? All[0];
}
