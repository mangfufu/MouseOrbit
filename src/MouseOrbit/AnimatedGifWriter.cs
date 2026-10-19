using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

// Stream frames to disk. Each frame carries its own palette. Recording memory stays
// bounded to the current capture rather than retaining a video-sized bitmap list.
internal sealed class AnimatedGifWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private bool _started, _finished;
    private int _width, _height;
    public int Frames { get; private set; }
    public AnimatedGifWriter(Stream stream) { _writer = new BinaryWriter(stream, Encoding.ASCII, false); }
    public void Add(BitmapSource source, int delayMilliseconds)
    {
        if (_finished) throw new ObjectDisposedException(nameof(AnimatedGifWriter));
        if (_started && (source.PixelWidth != _width || source.PixelHeight != _height)) throw new InvalidDataException("GIF frame size changed");
        var frame = new FormatConvertedBitmap(source, PixelFormats.Indexed8, BitmapPalettes.Halftone256, 0);
        var encoder = new GifBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
        using var memory = new MemoryStream(); encoder.Save(memory); byte[] data = memory.ToArray();
        int packed = data[10], globalSize = (packed & 0x80) == 0 ? 0 : 3 * (1 << ((packed & 7) + 1));
        byte[] palette = globalSize == 0 ? [] : data.Skip(13).Take(globalSize).ToArray(); int paletteBits = packed & 7;
        int offset = 13 + globalSize;
        while (offset < data.Length && data[offset] == 0x21)
        {
            offset += 2;
            while (offset < data.Length) { int size = data[offset++]; if (size == 0) break; offset += size; }
        }
        if (offset + 10 >= data.Length || data[offset] != 0x2C) throw new InvalidDataException("GIF frame missing");
        int descriptor = offset; offset += 10; int localPacked = data[descriptor + 9];
        if ((localPacked & 0x80) != 0)
        { paletteBits = localPacked & 7; int length = 3 * (1 << (paletteBits + 1)); palette = data.Skip(offset).Take(length).ToArray(); offset += length; }
        if (palette.Length == 0) throw new InvalidDataException("GIF palette missing");
        if (!_started)
        {
            _width = source.PixelWidth; _height = source.PixelHeight;
            _writer.Write(Encoding.ASCII.GetBytes("GIF89a")); _writer.Write((ushort)_width); _writer.Write((ushort)_height);
            _writer.Write(new byte[] { 0x70, 0, 0 });
            _writer.Write(new byte[] { 0x21, 0xFF, 0x0B }); _writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); _writer.Write(new byte[] { 3, 1, 0, 0, 0 }); _started = true;
        }
        _writer.Write(new byte[] { 0x21, 0xF9, 4, 4 }); _writer.Write((ushort)Math.Clamp(delayMilliseconds / 10, 2, ushort.MaxValue)); _writer.Write(new byte[] { 0, 0 });
        _writer.Write((byte)0x2C); _writer.Write((ushort)0); _writer.Write((ushort)0); _writer.Write((ushort)_width); _writer.Write((ushort)_height);
        _writer.Write((byte)(0x80 | (localPacked & 0x40) | paletteBits)); _writer.Write(palette);
        int start = offset++; // minimum LZW code size followed by data sub-blocks
        while (offset < data.Length) { int size = data[offset++]; if (size == 0) break; offset += size; }
        _writer.Write(data, start, offset - start); Frames++;
    }
    public void Dispose()
    { if (_finished) return; _finished = true; if (_started) _writer.Write((byte)0x3B); _writer.Dispose(); }
}
