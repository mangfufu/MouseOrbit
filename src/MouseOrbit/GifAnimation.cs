using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

// Read recorded frames on demand. Export one annotated frame at a time to keep memory bounded.
internal sealed class GifAnimation : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private readonly GifBitmapDecoder _decoder;
    public int Count => _decoder.Frames.Count;
    public GifAnimation(string path)
    {
        _path = path; _stream = File.OpenRead(path);
        try
        {
            _decoder = new(_stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            if (Count == 0) throw new InvalidDataException("GIF 没有画面");
        }
        catch { _stream.Dispose(); throw; }
    }
    public BitmapSource Frame(int index) => _decoder.Frames[index];
    public int Delay(int index)
    {
        try
        {
            if (_decoder.Frames[index].Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/grctlext/Delay"))
                return Math.Max(20, Convert.ToInt32(metadata.GetQuery("/grctlext/Delay")) * 10);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { }
        return GifRecorderWindow.FrameMilliseconds;
    }
    internal BitmapSource Render(int index, IEnumerable<Markup> marks)
    {
        var document = new CaptureDocument(Frame(index)); document.Marks.AddRange(marks); return document.Render();
    }
    public async Task Export(string path, IReadOnlyList<Markup> marks, Action<int, int>? progress = null)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".mouseorbit-" + Guid.NewGuid().ToString("N") + ".gif");
        try
        {
            if (marks.Count == 0)
            {
                await Task.Run(() => File.Copy(_path, temporary));
                progress?.Invoke(Count, Count);
            }
            else
            {
                using var writer = new AnimatedGifWriter(File.Create(temporary));
                for (int i = 0; i < Count; i++)
                {
                    var frame = Render(i, marks); int delay = Delay(i);
                    await Task.Run(() => writer.Add(frame, delay));
                    progress?.Invoke(i + 1, Count);
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => _stream.Dispose();
}
