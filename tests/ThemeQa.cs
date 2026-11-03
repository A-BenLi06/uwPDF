using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        // Own-app native tests. No system preference changes or desktop input.
        public async void RunThemeQa()
        {
            var report = new StringBuilder();
            var output = await ApplicationData.Current.LocalFolder.GetFolderAsync("theme-qa");
            try
            {
                await LoadDocumentAsync(await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///TestAssets/SkimSample.pdf")));
                await Task.Delay(600);
                var zoom = DocumentScroller.ZoomFactor;
                var height = DocumentScroller.ViewportHeight;
                var dialog = new ContentDialog(); BindReaderTheme(dialog);
                foreach (var dark in new[] { false, true, false, true })
                {
                    RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                    ApplyReaderTheme(dark, false);
                    await Task.Delay(150);
                    var surface = ((SolidColorBrush)ReaderToolbar.Background).Color;
                    var text = ((SolidColorBrush)FileNameText.Foreground).Color;
                    ThemeAssert(dark ? surface.R < 60 && text.R > 220 : surface.R > 220 && text.R < 70,
                        "Native XAML toolbar/text theme resources");
                    ThemeAssert(((SolidColorBrush)SearchPanel.Background).Color == surface,
                        "Search overlay matches toolbar");
                    var title = ApplicationView.GetForCurrentView().TitleBar;
                    var accent = themeSettings.GetColorValue(UIColorType.Accent);
                    ThemeAssert(title.BackgroundColor == accent && title.ForegroundColor == CaptionTextColor(accent)
                        && title.InactiveBackgroundColor == surface, "Native titlebar uses Windows accent and themed inactive surface");
                    ThemeAssert(dialog.RequestedTheme == RequestedTheme, "Dialog live theme binding");
                    ThemeAssert(dark ? ThumbIdleTextBrush.Color.R > 150 : ThumbIdleTextBrush.Color.R < 150,
                        "Code-created thumbnail labels update");
                    ThemeAssert(PageFillBrush.Color == Colors.White && DocumentScroller.ZoomFactor == zoom &&
                        DocumentScroller.ViewportHeight == height, "PDF page color, zoom and viewport preserved");
                    ZoomInputFlyout.ShowAt(ZoomPercentButton);
                    await Task.Delay(100);
                    ThemeAssert(((SolidColorBrush)ZoomLabel.Foreground).Color.R > 220,
                        "Dark HUD remains readable in both themes");
                    ZoomInputFlyout.Hide();
                    SetAnnotationToolbarOpen(true);
                    SearchPanel.Visibility = Visibility.Visible;
                    PenFlyout.ShowAt(TopPenButton);
                    await Task.Delay(100);
                    ThemeAssert(((SolidColorBrush)PenSizeText.Foreground).Color.R == ThumbIdleTextBrush.Color.R,
                        "Pen settings popup follows theme");
                    PenFlyout.Hide();
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                    await bitmap.RenderAsync(MainChrome);
                    using (var stream = await (await output.CreateFileAsync(dark ? "dark.png" : "light.png",
                        CreationCollisionOption.ReplaceExisting)).OpenAsync(FileAccessMode.ReadWrite))
                    {
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                            Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                        var pixels = await bitmap.GetPixelsAsync();
                        var data = new byte[pixels.Length];
                        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels)) reader.ReadBytes(data);
                        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, data);
                        await encoder.FlushAsync();
                    }
                    report.AppendLine("PASS: " + (dark ? "Dark" : "Light") + " toolbar/search/titlebar/thumbnails/PDF/HUD");
                }
                StopThemeMonitoring(); StartThemeMonitoring();
                foreach (var caption in new[] { Colors.Black, Colors.White, Colors.Yellow, Colors.Red,
                    Colors.Blue, Colors.Green, Color.FromArgb(255, 128, 128, 128), Color.FromArgb(255, 0, 120, 215) })
                {
                    var luminance = CaptionLuminance(caption);
                    var ink = CaptionTextColor(caption);
                    var ratio = ink == Colors.White ? 1.05 / (luminance + 0.05) : (luminance + 0.05) / 0.05;
                    ThemeAssert(ratio >= 4.5, "Readable title text for light, dark and saturated system accents");
                }
                ThemeAssert(themeSettings != null && themeAccessibility != null, "Monitoring resumes after navigation");
                var settings = themeSettings;
                SystemThemeChanged(settings, null);
                await Task.Delay(100);
                ThemeAssert(RequestedTheme == (5 * settings.GetColorValue(UIColorType.Foreground).G +
                    2 * settings.GetColorValue(UIColorType.Foreground).R + settings.GetColorValue(UIColorType.Foreground).B > 1024
                    ? ElementTheme.Dark : ElementTheme.Light), "System event restores Windows theme");
                report.AppendLine("PASS: system event and lifecycle");
                report.AppendLine("PASS: theme QA completed");
            }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex); }
            finally { if (themeSettings != null) ApplySystemTheme(); }
            await FileIO.WriteTextAsync(await output.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting), report.ToString());
        }

        private static void ThemeAssert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
