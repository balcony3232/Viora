using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Viora;

/// <summary>
/// Dependency-free WPF GIF reader with frame timing, positional compositing and
/// disposal handling. Files are read only; preview/splash timers stop on Dispose.
/// </summary>
public sealed class GifPlayback : IDisposable
{
    private readonly Image _image;
    private readonly DispatcherTimer _clock = new();
    private readonly List<BitmapSource> _frames = new();
    private readonly List<TimeSpan> _delays = new();
    private int _index;

    public GifPlayback(Image image)
    {
        _image = image;
        _clock.Tick += NextFrame;
    }

    public void Stop()
    {
        _clock.Stop();
        _frames.Clear();
        _delays.Clear();
        _index = 0;
        _image.Source = null;
    }

    public void Open(string path)
    {
        Stop();
        if (!File.Exists(path)) throw new FileNotFoundException("The GIF was removed or moved.", path);
        var file = new FileInfo(path);
        if (file.Length > 15 * 1024 * 1024) throw new InvalidDataException("GIFs must be smaller than 15 MB.");

        var decoder = new GifBitmapDecoder(new Uri(file.FullName),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("This GIF contains no frames.");
        if (decoder.Frames.Count > 240) throw new InvalidDataException("Please use a GIF with 240 frames or fewer.");
        int width = ReadMetadataInt(decoder.Metadata as BitmapMetadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth);
        int height = ReadMetadataInt(decoder.Metadata as BitmapMetadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
        if (width < 1 || height < 1 || width > 1920 || height > 1080 ||
            (long)width * height * decoder.Frames.Count > 60_000_000)
            throw new InvalidDataException("This GIF is too large to animate safely. Try reducing its dimensions or frame count.");

        BitmapSource? baseFrame = null;
        foreach (var frame in decoder.Frames)
        {
            var metadata = frame.Metadata as BitmapMetadata;
            int x = Math.Clamp(ReadMetadataInt(metadata, "/imgdesc/Left", 0), 0, width - 1);
            int y = Math.Clamp(ReadMetadataInt(metadata, "/imgdesc/Top", 0), 0, height - 1);
            int disposal = ReadMetadataInt(metadata, "/grctlext/Disposal", 0);
            var completed = Composite(baseFrame, frame, width, height, x, y);
            _frames.Add(completed);
            _delays.Add(ReadDelay(metadata));
            switch (disposal)
            {
                case 2: // Restore to background colour; transparent suits the preview panel.
                    baseFrame = ClearArea(completed, x, y,
                        Math.Min(frame.PixelWidth, width - x), Math.Min(frame.PixelHeight, height - y));
                    break;
                case 3: // Restore previous frame: keep the pre-composition base.
                    break;
                default: // Unspecified or leave in place.
                    baseFrame = completed;
                    break;
            }
        }
        _image.Source = _frames[0];
        if (_frames.Count > 1)
        {
            _index = 0;
            _clock.Interval = _delays[0];
            _clock.Start();
        }
    }

    private static int ReadMetadataInt(BitmapMetadata? metadata, string key, int fallback)
    {
        try
        {
            if (metadata?.ContainsQuery(key) == true)
                return Convert.ToInt32(metadata.GetQuery(key), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { /* Missing or malformed optional metadata. */ }
        return fallback;
    }

    private static TimeSpan ReadDelay(BitmapMetadata? metadata)
    {
        int hundredths = ReadMetadataInt(metadata, "/grctlext/Delay", 10);
        if (hundredths <= 0) hundredths = 10;
        return TimeSpan.FromMilliseconds(Math.Clamp(hundredths * 10, 30, 10000));
    }

    private static BitmapSource Composite(BitmapSource? previous, BitmapSource raw, int width, int height, int x, int y)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (previous is not null) drawing.DrawImage(previous, new Rect(0, 0, width, height));
            drawing.DrawImage(raw, new Rect(x, y, raw.PixelWidth, raw.PixelHeight));
        }
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static BitmapSource ClearArea(BitmapSource source, int x, int y, int width, int height)
    {
        if (width < 1 || height < 1) return source;
        var mutable = new WriteableBitmap(source);
        var pixels = new byte[width * height * 4];
        mutable.WritePixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        mutable.Freeze();
        return mutable;
    }

    private void NextFrame(object? sender, EventArgs e)
    {
        if (_frames.Count < 2) return;
        _index = (_index + 1) % _frames.Count;
        _image.Source = _frames[_index];
        _clock.Interval = _delays[_index];
    }

    public void Dispose() => Stop();
}
