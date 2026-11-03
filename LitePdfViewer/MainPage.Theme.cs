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

            RestoreSystemTitleBarColors();
            ApplyOpenPopupThemes();
        }

        private static void RestoreSystemTitleBarColors()
        {
            // Null delegates to Windows, including its accent-on-titlebars switch,
            // activation, high contrast and caption-button states. Transparent is
            // not a system default: native titlebars ignore the alpha channel.
            var title = ApplicationView.GetForCurrentView().TitleBar;
            title.BackgroundColor = null;
            title.ForegroundColor = null;
            title.InactiveBackgroundColor = null;
            title.InactiveForegroundColor = null;
            title.ButtonBackgroundColor = null;
            title.ButtonForegroundColor = null;
            title.ButtonInactiveBackgroundColor = null;
            title.ButtonInactiveForegroundColor = null;
            title.ButtonHoverBackgroundColor = null;
            title.ButtonHoverForegroundColor = null;
            title.ButtonPressedBackgroundColor = null;
            title.ButtonPressedForegroundColor = null;
        }
    }
}
