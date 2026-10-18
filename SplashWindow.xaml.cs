using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Viora;

/// <summary>Compact forest splash; optional custom image/GIF uses the same floating-window layout.</summary>
public partial class SplashWindow : Window
{
    private readonly SplashOptions _options;
    private readonly bool _reduceMotion;
    private readonly GifPlayback _player;
    private readonly DispatcherTimer _dotsTimer;
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _dotFrame;

    // Windows 11 can round an opaque, hardware-rendered window without forcing
    // WPF to render all text through a transparency layer.
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);

    private void PreferNativeRoundedCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        const int DwmWindowCornerPreference = 33;
        int round = 2;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
    }


    public SplashWindow(SplashOptions options, bool reduceMotion)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            PreferNativeRoundedCorners();
        };
        _options = options.Clone();
        _reduceMotion = reduceMotion;
        bool defaultSplash = string.Equals(options.Mode, "Default", StringComparison.OrdinalIgnoreCase);
        DefaultForest.Visibility = defaultSplash ? Visibility.Visible : Visibility.Collapsed;
        Outer.Visibility = defaultSplash ? Visibility.Collapsed : Visibility.Visible;
        if (defaultSplash)
        {
            // Keep the branded default compact, regardless of a previously saved splash size.
            WindowState = WindowState.Normal;
            Width = 460;
            Height = 340;
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };
        }
        else (Width, Height) = options.Size switch
        {
            "Medium" => (560d, 395d),
            "Large" => (660d, 460d),
            _ => (460d, 340d)
        };
        BannerLogo.Visibility = options.ShowLogo && options.Mode == "Default" ? Visibility.Visible : Visibility.Collapsed;
        BrandLogo.Visibility = options.ShowLogo ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Visibility = options.ShowText ? Visibility.Visible : Visibility.Collapsed;
        if (!defaultSplash && !string.IsNullOrWhiteSpace(options.BackgroundFile) && File.Exists(options.BackgroundFile))
            BannerBackground.Source = ReadStatic(options.BackgroundFile);

        _player = new GifPlayback(BannerMedia);
        if (options.Mode == "Gif" && File.Exists(options.CustomMediaFile))
            _player.Open(options.CustomMediaFile);
        else if (options.Mode == "Image" && File.Exists(options.CustomMediaFile))
            BannerMedia.Source = ReadStatic(options.CustomMediaFile);
        else if (options.Mode != "Default")
            BannerLogo.Visibility = options.ShowLogo ? Visibility.Visible : Visibility.Collapsed;

        _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _dotsTimer.Tick += (_, _) =>
        {
            _dotFrame = (_dotFrame + 1) % 4;
            LoadingDots.Text = "●  ●  ●".Substring(0, _dotFrame == 0 ? 7 : Math.Min(7, 1 + (_dotFrame - 1) * 3));
        };
        Closed += (_, _) =>
        {
            _dotsTimer.Stop();
            _player.Dispose();
            _closed.TrySetResult(true);
        };
    }

    private static BitmapImage ReadStatic(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1200;
        bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public async Task RunAsync()
    {
        _dotsTimer.Start();
        if (_options.Fade && !_reduceMotion) await FadeToAsync(0, 1, _options.FadeInMilliseconds);
        await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.DurationSeconds, 0.5, 10))), _closed.Task);
        if (!_closed.Task.IsCompleted && _options.Fade && !_reduceMotion)
            await FadeToAsync(1, 0, _options.FadeOutMilliseconds);
    }

    private async Task FadeToAsync(double from, double to, int ms)
    {
        if (_closed.Task.IsCompleted) return;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, 1500)))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) => completed.TrySetResult(true);
        BeginAnimation(OpacityProperty, animation);
        await Task.WhenAny(completed.Task, _closed.Task);
    }

    private void CloseSplash_Click(object sender, RoutedEventArgs e) => Close();
}
