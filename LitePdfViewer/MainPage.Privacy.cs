using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private bool privacyDialogOpen;

        private async void PrivacyCommand_Click(object sender, RoutedEventArgs e)
        {
            if (privacyDialogOpen || historyOpen || ocrDialogOpen || isPromptingSave) return;
            privacyDialogOpen = true;
            HideReaderMore();
            try
            {
                var text = new TextBlock { Text = L.Get("PrivacyBody"), TextWrapping = TextWrapping.Wrap,
                    Width = 420, MaxWidth = Math.Max(180, Window.Current.Bounds.Width - 96) };
                var dialog = new ContentDialog { RequestedTheme = RequestedTheme, Title = L.Get("PrivacyTitle"),
                    Content = new ScrollViewer { Content = text, MaxHeight = Math.Max(120, Window.Current.Bounds.Height - 160),
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                    PrimaryButtonText = L.Get("Close"), SecondaryButtonText = L.Get("PrivacySupport") };
                if (await ShowReaderDialogAsync(dialog) == ContentDialogResult.Secondary)
                    await Launcher.LaunchUriAsync(new Uri("https://github.com/A-BenLi06/uwPDF/issues"));
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog(ex.Message, L.Get("ErrorTitle")).ShowAsync();
            }
            finally { privacyDialogOpen = false; }
        }
    }
}
