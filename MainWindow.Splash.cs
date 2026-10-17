using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Viora;

public partial class MainWindow
{
    private SplashOptions _splashDraft = new();
    private bool _loadingSplashEditor;
    private GifPlayback? _splashPreviewGif;
    private string _previewAnimatedFile = "";

    private void InitializeSplashEditor()
    {
        _state.Splash ??= new SplashOptions();
        // Preserve a v0.5.1 user's selected splash background on first upgrade.
        if (string.IsNullOrEmpty(_state.Splash.CustomMediaFile) &&
            _state.Splash.Mode == "Default" &&
            !string.IsNullOrWhiteSpace(_state.SplashBackgroundFile) &&
            File.Exists(_state.SplashBackgroundFile))
        {
            _state.Splash.BackgroundFile = _state.SplashBackgroundFile;
        }
        _splashPreviewGif ??= new GifPlayback(SplashPreviewImage);
        ShowSplashOptionsInEditor(_state.Splash);
    }

    private static void SelectTagged(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items[0];
    }

    private void ShowSplashOptionsInEditor(SplashOptions options)
    {
        _loadingSplashEditor = true;
        _splashDraft = options.Clone();
        SplashEnable.IsChecked = options.Enabled;
        SelectTagged(SplashMode, options.Mode);
        SelectTagged(SplashSize, options.Size);
        SplashDurationSlider.Value = Math.Clamp(options.DurationSeconds, 0.5, 10.0);
        SplashShowLogo.IsChecked = options.ShowLogo;
        SplashShowText.IsChecked = options.ShowText;
        SplashSkipRunning.IsChecked = options.SkipIfRobloxRunning;
        SplashFade.IsChecked = options.Fade;
        SelectTagged(SplashFadeIn, options.FadeInMilliseconds.ToString(CultureInfo.InvariantCulture));
        SelectTagged(SplashFadeOut, options.FadeOutMilliseconds.ToString(CultureInfo.InvariantCulture));
        _loadingSplashEditor = false;
        RefreshSplashPreview();
    }

    private static string ComboTag(ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private SplashOptions ReadSplashEditor()
    {
        var result = _splashDraft.Clone();
        result.Enabled = SplashEnable.IsChecked == true;
        result.Mode = ComboTag(SplashMode, "Default");
        result.Size = ComboTag(SplashSize, "Small");
        result.DurationSeconds = Math.Clamp(SplashDurationSlider.Value, 0.5, 10);
        result.ShowLogo = SplashShowLogo.IsChecked == true;
        result.ShowText = SplashShowText.IsChecked == true;
        result.SkipIfRobloxRunning = SplashSkipRunning.IsChecked == true;
        result.Fade = SplashFade.IsChecked == true;
        result.FadeInMilliseconds = int.TryParse(ComboTag(SplashFadeIn, "300"), out int fadeIn) ? fadeIn : 300;
        result.FadeOutMilliseconds = int.TryParse(ComboTag(SplashFadeOut, "300"), out int fadeOut) ? fadeOut : 300;
        return result;
    }

    private void SplashPreviewInput_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loadingSplashEditor || _splashPreviewGif is null) return;
        RefreshSplashPreview();
    }

    private void RefreshSplashPreview()
    {
        if (_loadingSplashEditor || _splashPreviewGif is null) return;
        var options = ReadSplashEditor();
        SplashDurationLabel.Text = options.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " seconds";
        SplashMediaLabel.Text = options.Mode == "Default" ? "Viora forest · full-screen" :
            string.IsNullOrWhiteSpace(options.CustomMediaFile) ? "No custom media selected" : Path.GetFileName(options.CustomMediaFile);
        SplashBackgroundLabel.Text = string.IsNullOrWhiteSpace(options.BackgroundFile) ? "Default dark background (custom modes)" : Path.GetFileName(options.BackgroundFile);
        bool isDefault = string.Equals(options.Mode, "Default", StringComparison.OrdinalIgnoreCase);
        SplashPreviewDefault.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
        SplashPreviewCompact.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        SplashSize.IsEnabled = !isDefault;
        SplashShowLogo.IsEnabled = !isDefault;
        SplashShowText.IsEnabled = !isDefault;
        SplashPreviewInfo.Text = (isDefault ? "Full-screen" : options.Size) + "  ·  " + options.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
            " seconds  ·  " + options.Mode + (options.Enabled ? "" : "  ·  Disabled");
        SplashPreviewSmallLogo.Visibility = options.ShowLogo ? Visibility.Visible : Visibility.Collapsed;
        SplashPreviewText.Visibility = options.ShowText ? Visibility.Visible : Visibility.Collapsed;
        SplashPreviewBannerLogo.Visibility = options.ShowLogo && options.Mode == "Default" ? Visibility.Visible : Visibility.Collapsed;
        SplashFadeIn.IsEnabled = SplashFadeOut.IsEnabled = options.Fade;

        try
        {
            SplashPreviewBackground.Source = TryReadSplashBitmap(options.BackgroundFile);
        }
        catch { SplashPreviewBackground.Source = null; }

        bool valid = options.Mode == "Gif" && File.Exists(options.CustomMediaFile) &&
            Path.GetExtension(options.CustomMediaFile).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        if (valid)
        {
            if (!string.Equals(options.CustomMediaFile, _previewAnimatedFile, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _splashPreviewGif.Open(options.CustomMediaFile);
                    _previewAnimatedFile = options.CustomMediaFile;
                }
                catch (Exception ex)
                {
                    _splashPreviewGif.Stop();
                    _previewAnimatedFile = "";
                    SplashMediaLabel.Text = "Cannot preview GIF: " + ex.Message;
                    SplashPreviewBannerLogo.Visibility = options.ShowLogo ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        else
        {
            _splashPreviewGif.Stop();
            _previewAnimatedFile = "";
            if (options.Mode == "Image" && File.Exists(options.CustomMediaFile))
            {
                try { SplashPreviewImage.Source = TryReadSplashBitmap(options.CustomMediaFile); }
                catch { SplashPreviewImage.Source = null; }
            }
            SplashPreviewBannerLogo.Visibility = options.ShowLogo && (options.Mode == "Default" || !File.Exists(options.CustomMediaFile))
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static BitmapImage? TryReadSplashBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 1200;
        image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string ValidateSplashFile(string path, bool gif)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected image no longer exists.", path);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (gif ? ext != ".gif" : ext is not (".png" or ".jpg" or ".jpeg"))
            throw new InvalidDataException(gif ? "Select an animated .gif file." : "Select a PNG or JPEG image.");
        long limit = gif ? 15 * 1024 * 1024 : 12 * 1024 * 1024;
        if (new FileInfo(path).Length > limit)
            throw new InvalidDataException("This image exceeds the " + (gif ? "15" : "12") + " MB limit.");
        if (!gif && TryReadSplashBitmap(path) is null)
            throw new InvalidDataException("The image cannot be decoded.");
        return path;
    }

    private void SplashChooseMedia_Click(object sender, RoutedEventArgs e)
    {
        var wanted = ComboTag(SplashMode, "Default");
        var filter = wanted == "Gif" ? "Animated GIF (*.gif)|*.gif" :
            wanted == "Image" ? "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" :
            "Images and animated GIFs (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif";
        var picker = new OpenFileDialog { Title = "Choose Viora splash artwork", Filter = filter };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            bool gif = Path.GetExtension(picker.FileName).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            ValidateSplashFile(picker.FileName, gif);
            if (gif)
            {
                using var test = new GifPlayback(new Image());
                test.Open(picker.FileName);
            }
            _splashDraft.CustomMediaFile = picker.FileName;
            SelectTagged(SplashMode, gif ? "Gif" : "Image");
            RefreshSplashPreview();
        }
        catch (Exception ex) { Alert("Could not use that file: " + ex.Message); }
    }

    private void SplashClearMedia_Click(object sender, RoutedEventArgs e)
    {
        _splashDraft.CustomMediaFile = "";
        SelectTagged(SplashMode, "Default");
        RefreshSplashPreview();
    }

    private void SplashChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose a splash background", Filter = "PNG / JPEG (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            _splashDraft.BackgroundFile = ValidateSplashFile(picker.FileName, gif: false);
            RefreshSplashPreview();
        }
        catch (Exception ex) { Alert("Could not use that background: " + ex.Message); }
    }

    private void SplashClearBackground_Click(object sender, RoutedEventArgs e)
    {
        _splashDraft.BackgroundFile = "";
        RefreshSplashPreview();
    }

    private static string StoreSplashAsset(string original)
    {
        if (string.IsNullOrWhiteSpace(original)) return "";
        string ext = Path.GetExtension(original).ToLowerInvariant();
        bool gif = ext == ".gif";
        ValidateSplashFile(original, gif);
        Directory.CreateDirectory(Path.Combine(Storage.Root, "splash"));
        byte[] hash;
        using (var input = File.OpenRead(original)) hash = SHA256.HashData(input);
        var dest = Path.Combine(Storage.Root, "splash", Convert.ToHexString(hash).ToLowerInvariant()[..16] + ext);
        if (!string.Equals(Path.GetFullPath(original), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(original, dest, overwrite: true);
        return dest;
    }

    private void SplashSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = ReadSplashEditor();
            if (options.Mode != "Default")
            {
                if (string.IsNullOrWhiteSpace(options.CustomMediaFile))
                    throw new InvalidDataException("Choose an image or GIF first, or switch back to Default mode.");
                bool wantGif = options.Mode == "Gif";
                ValidateSplashFile(options.CustomMediaFile, wantGif);
                options.CustomMediaFile = StoreSplashAsset(options.CustomMediaFile);
            }
            if (!string.IsNullOrWhiteSpace(options.BackgroundFile))
            {
                ValidateSplashFile(options.BackgroundFile, gif: false);
                options.BackgroundFile = StoreSplashAsset(options.BackgroundFile);
            }
            _state.Splash = options;
            _state.SplashBackgroundFile = options.BackgroundFile; // For compatibility with existing settings.
            Storage.Save(_state);
            ShowSplashOptionsInEditor(options);
            SetStatus("Splash settings saved. Restart Viora to see the new startup screen.");
        }
        catch (Exception ex) { Alert("Could not save splash settings: " + ex.Message); }
    }

    private async void SplashTest_Click(object sender, RoutedEventArgs e)
    {
        try { await ShowSplashWindowAsync(ReadSplashEditor(), preview: true); }
        catch (Exception ex) { Alert("Could not preview splash: " + ex.Message); }
    }

    private void SplashReset_Click(object sender, RoutedEventArgs e)
    {
        ShowSplashOptionsInEditor(new SplashOptions());
        SetStatus("Default splash settings are in the editor. Choose Save to keep them.");
    }

    private async Task ShowSplashWindowAsync(SplashOptions options, bool preview)
    {
        var shouldHideMain = !preview && IsVisible;
        SplashWindow splash = new(options, _state.ReduceMotion);
        try
        {
            if (shouldHideMain) Hide();
            splash.Show();
            await splash.RunAsync();
        }
        finally
        {
            if (splash.IsVisible) splash.Close();
            if (shouldHideMain)
            {
                Show();
                Activate();
            }
        }
    }

    private async Task ShowSplashAsync()
    {
        if (!_state.Splash.Enabled) return;
        if (_state.Splash.SkipIfRobloxRunning)
        {
            try
            {
                var running = Process.GetProcessesByName("RobloxPlayerBeta");
                if (running.Length != 0) { foreach (var item in running) item.Dispose(); return; }
                foreach (var item in running) item.Dispose();
            }
            catch { /* Best effort check. */ }
        }
        // Prevent hiding the main window before its first Loaded event (automatic sign-in).
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        try { await ShowSplashWindowAsync(_state.Splash, preview: false); }
        catch (Exception ex) { SetStatus("Splash preview could not open: " + ex.Message); }
    }

    private void NavSplash_Click(object sender, RoutedEventArgs e) => SwitchPage("splash");
}
