using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

public sealed class EntryIcon : Image
{
    private static readonly DependencyProperty ToneProperty = DependencyProperty.Register("Tone", typeof(Color), typeof(EntryIcon), new PropertyMetadata(Colors.Transparent, (sender, _) =>
    {
        var image = (EntryIcon)sender;
        if (image.Entry?.Kind == "command") image.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(image.RefreshCommand));
    }));
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(nameof(Entry), typeof(DesktopEntry), typeof(EntryIcon), new PropertyMetadata(null, Changed));
    public DesktopEntry? Entry { get => (DesktopEntry?)GetValue(EntryProperty); set => SetValue(EntryProperty, value); }
    public EntryIcon()
    {
        Stretch = Stretch.Uniform; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        System.Windows.Data.BindingOperations.SetBinding(this, ToneProperty, new System.Windows.Data.Binding(nameof(SolidColorBrush.Color)) { Source = Palette.Ink });
        Loaded += (_, _) => Palette.Changed += RefreshCommand;
        Unloaded += (_, _) => Palette.Changed -= RefreshCommand;
    }
    private void RefreshCommand() { if (Entry?.Kind == "command") Source = CommandIcons.Get(Entry.Target); }
    private static async void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var image = (EntryIcon)sender;
        var entry = args.NewValue as DesktopEntry;
        if (entry != null && entry.Kind != "command" && ShellIcons.TryGet(entry, out var cached))
        { image.Source = cached ?? CommandIcons.Get(entry.Kind == "folder" ? "folder" : entry.Kind == "window" ? "window" : "file"); return; }
        image.Source = CommandIcons.Get(entry?.Kind == "command" ? entry.Target : entry?.Kind == "folder" ? "folder" : entry?.Kind == "window" ? "window" : "file");
        if (entry == null || entry.Kind == "command") return;
        var result = await ShellIcons.Load(entry).ConfigureAwait(false);
        if (!image.Dispatcher.HasShutdownStarted)
            _ = image.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (ReferenceEquals(image.Entry, entry) && result != null) image.Source = result;
            }));
    }
}

internal static class ShellIcons
{
    private static readonly IconCache Cache = new();
    private static long _queuedReads;
    internal static long QueuedReads => Interlocked.Read(ref _queuedReads);
    private static readonly BlockingCollection<(DesktopEntry Entry, TaskCompletionSource<ImageSource?> Completion)> Queue = new();
    static ShellIcons()
    {
        for (int i = 0; i < 2; i++)
        {
            var thread = new Thread(() =>
            {
                int com = CoInitializeEx(0, 2);
                try
                {
                    foreach (var work in Queue.GetConsumingEnumerable())
                    {
                        try { work.Completion.TrySetResult(Read(work.Entry)); }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or COMException or IOException or UnauthorizedAccessException or NotSupportedException)
                        { work.Completion.TrySetResult(null); }
                    }
                }
                finally { if (com >= 0) CoUninitialize(); }
            }) { IsBackground = true, Name = "MouseOrbit icons", Priority = ThreadPriority.BelowNormal };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string path, int index, uint flags, out nint large, out nint small, uint size);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nuint parameter, nint data, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    public static Task<ImageSource?> Load(DesktopEntry entry)
    {
        return Cache.Get(Key(entry), () =>
        {
            var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _queuedReads);
            Queue.Add((entry, completion)); return completion.Task;
        });
    }
    private static string Key(DesktopEntry entry) => entry.Kind + ":" + entry.Target;
    internal static bool TryGet(DesktopEntry entry, out ImageSource? image) => Cache.TryGet(Key(entry), out image);
    internal static Task PreloadAsync(IEnumerable<DesktopEntry> entries) => Task.WhenAll(entries.Where(e => e.Kind != "command")
        .DistinctBy(Key, StringComparer.OrdinalIgnoreCase).Select(Load));
    private static ImageSource? Read(DesktopEntry entry)
    {
        var path = entry.Target;
        if (entry.Kind == "window" && long.TryParse(path, out long value))
        {
            nint window = (nint)value, icon = 0;
            foreach (nuint kind in new nuint[] { 1, 2, 0 })
            {
                SendMessageTimeout(window, 0x7F, kind, 0, 2, 100, out icon);
                if (icon != 0) break;
            }
            if (icon == 0 && Environment.Is64BitProcess) icon = GetClassLongPtr(window, -14);
            if (icon != 0) return FromOwnedHandle(CopyIcon(icon));
            GetWindowThreadProcessId(window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            path = process.MainModule?.FileName ?? "";
        }
        if (string.IsNullOrWhiteSpace(path) || !(File.Exists(path) || Directory.Exists(path))) return null;
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var link = (IShellLink)new ShellLink();
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
                var location = new StringBuilder(1024); link.GetIconLocation(location, location.Capacity, out int index);
                var iconPath = Environment.ExpandEnvironmentVariables(location.ToString()).Trim('"');
                if (File.Exists(iconPath) && Extract(iconPath, index) is { } custom) return custom;
                location.Clear(); link.GetPath(location, location.Capacity, 0, 0);
                var target = Environment.ExpandEnvironmentVariables(location.ToString());
                if (File.Exists(target) || Directory.Exists(target)) path = target;
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        if (new[] { ".exe", ".dll", ".ico" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && Extract(path, 0) is { } extracted) return extracted;
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x100);
        return FromOwnedHandle(info.Icon);
    }
    private static ImageSource? Extract(string path, int index)
    {
        SHDefExtractIcon(path, index, 0, out nint large, out nint small, 64);
        if (small != 0) DestroyIcon(small);
        return FromOwnedHandle(large);
    }
    private static ImageSource? FromOwnedHandle(nint icon)
    {
        if (icon == 0) return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze(); return bitmap;
        }
        finally { DestroyIcon(icon); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint data, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
