using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private UISettings themeSettings;
        private AccessibilitySettings themeAccessibility;
        private bool themeFlyoutsInitialized;

        private void ApplyOpenPopupThemes()
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopups(Window.Current))
            {
                var root = popup.Child as FrameworkElement;
                if (root == null) continue;
                // The HUD and its zoom editor are intentionally dark in either theme.
                if (ZoomInputBox != null && IsPopupAncestor(root, ZoomInputBox))
                    root.RequestedTheme = ElementTheme.Dark;
                else BindReaderTheme(root);
            }
        }

        private static bool IsPopupAncestor(DependencyObject root, DependencyObject child)
        {
            while (child != null)
            {
                if (child == root) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private void BindReaderTheme(FrameworkElement element)
        {
            element.SetBinding(FrameworkElement.RequestedThemeProperty,
                new Windows.UI.Xaml.Data.Binding { Source = this,
                    Path = new PropertyPath("RequestedTheme"), Mode = Windows.UI.Xaml.Data.BindingMode.OneWay });
        }

        private async System.Threading.Tasks.Task<Windows.UI.Xaml.Controls.ContentDialogResult> ShowReaderDialogAsync(
            Windows.UI.Xaml.Controls.ContentDialog dialog)
        {
            BindReaderTheme(dialog);
            return await dialog.ShowAsync();
        }

        private void StartThemeMonitoring()
        {
            if (themeSettings != null) return;
            if (!themeFlyoutsInitialized)
            {
                themeFlyoutsInitialized = true;
                PenFlyout.Opened += (s, e) => ApplyOpenPopupThemes();
                HighlighterFlyout.Opened += (s, e) => ApplyOpenPopupThemes();
                ZoomInputFlyout.Opened += (s, e) => ApplyOpenPopupThemes();
                ReaderMoreButton.Flyout.Opened += (s, e) => ApplyOpenPopupThemes();
                InkMoreButton.Flyout.Opened += (s, e) => ApplyOpenPopupThemes();
            }
            themeSettings = new UISettings();
            themeAccessibility = new AccessibilitySettings();
            themeSettings.ColorValuesChanged += SystemThemeChanged;
            themeAccessibility.HighContrastChanged += SystemHighContrastChanged;
            ApplySystemTheme();
        }

        private void StopThemeMonitoring()
        {
            if (themeSettings == null) return;
            themeSettings.ColorValuesChanged -= SystemThemeChanged;
            themeAccessibility.HighContrastChanged -= SystemHighContrastChanged;
            themeSettings = null;
            themeAccessibility = null;
        }

        private async void SystemThemeChanged(UISettings sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (themeSettings == sender) ApplySystemTheme();
            });
        }

        private async void SystemHighContrastChanged(AccessibilitySettings sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (themeAccessibility == sender) ApplySystemTheme();
            });
        }

        private void ApplySystemTheme()
        {
            // Compatible with the 14393 SDK: ActualThemeChanged is a newer API.
            var foreground = themeSettings.GetColorValue(UIColorType.Foreground);
            var dark = 5 * foreground.G + 2 * foreground.R + foreground.B > 8 * 128;
            RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            var highContrast = themeAccessibility.HighContrast;
            ApplyReaderTheme(dark, highContrast);
        }

        private void ApplyReaderTheme(bool dark, bool highContrast)
        {
            var palette = Application.Current.Resources.ThemeDictionaries[
                highContrast ? "HighContrast" : dark ? "Default" : "Light"] as ResourceDictionary;
            // Code-created thumbnail elements share these brushes, so update existing
            // and future thumbnails without rebuilding pages or discarding PDF rasters.
            ThumbIdleTextBrush.Color = ((SolidColorBrush)palette["ReaderSecondaryTextBrush"]).Color;
            ThumbIdleBorderBrush.Color = ((SolidColorBrush)palette["ReaderBorderBrush"]).Color;
            var accent = highContrast ? themeSettings.UIElementColor(UIElementType.Highlight)
                : themeSettings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.Accent);
            ThumbSelectedTextBrush.Color = accent;
            ThumbSelectedBorderBrush.Color = accent;

            var background = ((SolidColorBrush)palette["ReaderSurfaceBrush"]).Color;
            var text = ((SolidColorBrush)palette["ReaderTextBrush"]).Color;
            var secondary = ((SolidColorBrush)palette["ReaderSecondaryTextBrush"]).Color;
            var title = ApplicationView.GetForCurrentView().TitleBar;
            var caption = highContrast ? background : themeSettings.GetColorValue(UIColorType.Accent);
            var captionText = highContrast ? text : CaptionTextColor(caption);
            title.BackgroundColor = caption;
            title.InactiveBackgroundColor = background;
            title.ForegroundColor = title.ButtonForegroundColor = captionText;
            title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = secondary;
            title.ButtonBackgroundColor = title.ButtonInactiveBackgroundColor = Colors.Transparent;
            title.ButtonHoverBackgroundColor = highContrast ? themeSettings.UIElementColor(UIElementType.Highlight)
                : MixCaptionColor(caption, captionText, 0.12);
            title.ButtonPressedBackgroundColor = highContrast ? themeSettings.UIElementColor(UIElementType.Highlight)
                : MixCaptionColor(caption, captionText, 0.20);
            title.ButtonHoverForegroundColor = highContrast ? themeSettings.UIElementColor(UIElementType.HighlightText)
                : CaptionTextColor(title.ButtonHoverBackgroundColor.Value);
            title.ButtonPressedForegroundColor = highContrast ? themeSettings.UIElementColor(UIElementType.HighlightText)
                : CaptionTextColor(title.ButtonPressedBackgroundColor.Value);
            ApplyOpenPopupThemes();
        }

        private static double CaptionLuminance(Color color)
        {
            return 0.2126 * CaptionChannel(color.R) + 0.7152 * CaptionChannel(color.G) + 0.0722 * CaptionChannel(color.B);
        }

        private static double CaptionChannel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        private static Color CaptionTextColor(Color background)
        {
            var luminance = CaptionLuminance(background);
            return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? Colors.Black : Colors.White;
        }

        private static Color MixCaptionColor(Color background, Color foreground, double amount)
        {
            return Color.FromArgb(255, (byte)(background.R * (1 - amount) + foreground.R * amount),
                (byte)(background.G * (1 - amount) + foreground.G * amount),
                (byte)(background.B * (1 - amount) + foreground.B * amount));
        }
    }
}
