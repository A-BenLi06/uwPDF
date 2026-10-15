using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private readonly System.Collections.Generic.HashSet<uint> forcedOcrPages = new System.Collections.Generic.HashSet<uint>();
        private bool ocrDialogOpen;
        private ContentDialog ocrDialog;
        private string ocrLanguage = ApplicationData.Current.LocalSettings.Values["OcrLanguage"] as string;
        private PdfTextSource CreateTextSource(StorageFile file)
        {
            var ocr = new PdfOcrSource(file, ocrLanguage);
            var forced = new System.Collections.Generic.HashSet<uint>(forcedOcrPages);
            return new PdfTextSource(file, ocr.ReadAsync, forced.Contains);
        }

        private async void OcrButton_Click(object sender, RoutedEventArgs args)
        {
            if (document == null || savingPdf || ocrDialogOpen || historyOpen) return;
            HideReaderMore();
            var token = activeRenderToken;
            var view = GetCurrentPageView();
            var languages = OcrEngine.AvailableRecognizerLanguages;
            if (languages.Count == 0)
            {
                await new Windows.UI.Popups.MessageDialog(L.Get("OcrMissingLanguages"), L.Get("OcrTitle")).ShowAsync();
                return;
            }
            var panel = new StackPanel { MaxWidth = Math.Max(180, Window.Current.Bounds.Width - 96) };
            panel.Children.Add(new TextBlock { Text = L.Get("OcrHelp"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            var chooser = new ComboBox { Header = L.Get("OcrLanguage"), ItemsSource = languages,
                DisplayMemberPath = "DisplayName", HorizontalAlignment = HorizontalAlignment.Stretch };
            var engine = PdfOcrSource.CreateEngine(ocrLanguage);
            for (var i = 0; i < languages.Count; i++)
                if (engine != null && languages[i].LanguageTag == engine.RecognizerLanguage.LanguageTag) chooser.SelectedIndex = i;
            if (chooser.SelectedIndex < 0) chooser.SelectedIndex = 0;
            panel.Children.Add(chooser);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
            panel.Children.Add(status);
            var recognize = new Button { Content = L.Get("OcrRecognize"), HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(recognize);
            var dialog = new ContentDialog { RequestedTheme = RequestedTheme, Title = L.Get("OcrTitle"), Content = new ScrollViewer { Content = panel, MaxHeight = Math.Max(120, Window.Current.Bounds.Height - 160), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, PrimaryButtonText = L.Get("Close") };
            CancellationTokenSource pending = null;
            recognize.Click += async (s, e) =>
            {
                if (token != activeRenderToken) return;
                if (pending != null) pending.Dispose();
                pending = new CancellationTokenSource();
                var cancellation = pending.Token;
                recognize.IsEnabled = chooser.IsEnabled = false;
                status.Text = L.Get("OcrWorking");
                try
                {
                    var language = (Windows.Globalization.Language)chooser.SelectedItem;
                    var source = new PdfOcrSource(readingCopy ?? currentFile, language.LanguageTag);
                    var glyphs = await source.ReadAsync(view.Index, cancellation);
                    if (token != activeRenderToken || cancellation.IsCancellationRequested) return;
                    ocrLanguage = language.LanguageTag;
                    ApplicationData.Current.LocalSettings.Values["OcrLanguage"] = ocrLanguage;
                    var previous = textSource;
                    forcedOcrPages.Add(view.Index);
                    var forced = new System.Collections.Generic.HashSet<uint>(forcedOcrPages);
                    textSource = new PdfTextSource(readingCopy ?? currentFile, source.ReadAsync, forced.Contains);
                    if (previous != null) previous.Dispose();
                    ClearTextSelection(); CancelSearchWork(); SearchStatus.Text = "";
                    foreach (var page in pageViews) { page.Glyphs = null; page.TextFailed = false; }
                    view.Glyphs = glyphs;
                    SetInkTool(InkTool.None);
                    status.Text = L.Format("OcrCompleted", glyphs.FindAll(g => g.FromOcr).Count);
                    if (view.Chrome != null) AutomationProperties.SetHelpText(view.Chrome, L.Get("OcrSelectionHelp"));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { status.Text = L.Format("OcrFailed", ex.Message); }
                finally { recognize.IsEnabled = chooser.IsEnabled = true; }
            };
            dialog.Closed += (s, e) => { if (pending != null) pending.Cancel(); };
            ocrDialogOpen = true; ocrDialog = dialog;
            try { await dialog.ShowAsync(); }
            finally { ocrDialogOpen = false; ocrDialog = null; if (pending != null) { pending.Cancel(); pending.Dispose(); pending = null; } }
        }
        private void HideReaderMore() { if (ReaderMoreButton.Flyout != null) ReaderMoreButton.Flyout.Hide(); }
    }
}
