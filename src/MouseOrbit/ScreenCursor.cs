using System.Runtime.InteropServices;

namespace MouseOrbit;

internal static class ScreenCursor
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo
    { public uint Size, Flags; public nint Cursor; public Native.Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo
    { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    internal static void Draw(System.Drawing.Graphics graphics, System.Drawing.Rectangle area)
    {
        var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & 1) == 0 || info.Cursor == 0) return;
        nint cursor = CopyIcon(info.Cursor);
        if (cursor == 0) return;
        try { DrawAt(graphics, area, cursor, info.Position); }
        finally { DestroyIcon(cursor); }
    }
    internal static void DrawAt(System.Drawing.Graphics graphics, System.Drawing.Rectangle area, nint cursor, Native.Point position)
    {
        if (!GetIconInfo(cursor, out var icon)) return;
        try
        {
            nint dc = graphics.GetHdc();
            try { DrawIconEx(dc, position.X - area.Left - (int)icon.HotspotX, position.Y - area.Top - (int)icon.HotspotY, cursor, 0, 0, 0, 0, 3); }
            finally { graphics.ReleaseHdc(dc); }
        }
        finally
        {
            if (icon.Mask != 0) DeleteObject(icon.Mask);
            if (icon.Color != 0) DeleteObject(icon.Color);
        }
    }
}
