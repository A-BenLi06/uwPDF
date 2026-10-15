using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private bool historyOpen;
        private ContentDialog historyDialog;
        private string historyWarning;
        private async void HistoryButton_Click(object sender, RoutedEventArgs args)
        {
            if (document == null || historyOpen || ocrDialogOpen || savingPdf) return;
            historyOpen = true; HideReaderMore();
            var token = activeRenderToken;
            try
            {
                if (saveInProgress != null) await saveInProgress;
                var entries = await PdfSnapshotStore.ListAsync(await GetAnnotationFolderAsync());
                if (token != activeRenderToken) return;
                var panel = new StackPanel { Width = 380, MaxWidth = Math.Max(180, Window.Current.Bounds.Width - 96) };
                panel.Children.Add(new TextBlock { Text = L.Get("HistoryHelp"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });
                var list = new ListView { ItemsSource = entries, MaxHeight = 128, SelectionMode = ListViewSelectionMode.Single };
                panel.Children.Add(list);
                var preview = new Image { Height = 200, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 8) };
                panel.Children.Add(preview);
                var status = new TextBlock { Text = entries.Count == 0 ? L.Get("HistoryEmpty") : "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
                panel.Children.Add(status);
                var pageRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var previous = new Button { Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE76B" } };
                var next = new Button { Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE76C" } };
                var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(previous, L.Get("HistoryPreviousPage"));
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(next, L.Get("HistoryNextPage"));
                pageRow.Children.Add(previous); pageRow.Children.Add(pageText); pageRow.Children.Add(next); panel.Children.Add(pageRow);
                var restore = new Button { Content = L.Get("HistoryRestore"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0), IsEnabled = false };
                panel.Children.Add(restore);
                var dialog = new ContentDialog { RequestedTheme = RequestedTheme, Title = L.Get("HistoryTitle"), Content = new ScrollViewer { Content = panel, MaxHeight = Math.Max(120, Window.Current.Bounds.Height - 160), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, PrimaryButtonText = L.Get("Close") };
                PdfSnapshot selected = null, requested = null;
                PdfDocument pdf = null;
                uint previewPage = 0;
                var revision = 0;
                Func<Task> show = async () =>
                {
                    var version = ++revision;
                    restore.IsEnabled = false;
                    try
                    {
                        if (selected == null) return;
                        var file = await selected.Folder.GetFileAsync("document.pdf");
                        var loaded = await PdfDocument.LoadFromFileAsync(file);
                        if (version != revision) return;
                        pdf = loaded;
                        previewPage = Math.Min(previewPage, pdf.PageCount - 1);
                        using (var page = loaded.GetPage(previewPage))
                        using (var stream = new InMemoryRandomAccessStream())
                        {
                            var scale = Math.Min(640 / page.Size.Width, 400 / page.Size.Height);
                            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale), DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale) });
                            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
                            if (version != revision) return;
                            preview.Source = bitmap;
                            pageText.Text = L.Format("HistoryPageCount", previewPage + 1, pdf.PageCount);
                            previous.IsEnabled = previewPage > 0; next.IsEnabled = previewPage + 1 < pdf.PageCount;
                            status.Text = L.Get(selected.Kind == "baseline" ? "HistoryBaseline" : selected.Kind == "rescue" ? "HistoryRescue" : "HistorySaved");
                            restore.IsEnabled = true;
                        }
                    }
                    catch (Exception ex) { if (version == revision) status.Text = L.Format("HistoryFailed", ex.Message); }
                };
                list.SelectionChanged += async (s, e) => { selected = list.SelectedItem as PdfSnapshot; previewPage = selected == null ? 0 : selected.Page; await show(); };
                previous.Click += async (s, e) => { if (previewPage > 0) { previewPage--; await show(); } };
                next.Click += async (s, e) => { if (pdf != null && previewPage + 1 < pdf.PageCount) { previewPage++; await show(); } };
                restore.Click += (s, e) => { requested = selected; dialog.Hide(); };
                dialog.Closed += (s, e) => { ++revision; pdf = null; };
                list.SelectedIndex = entries.Count > 0 ? 0 : -1;
                historyDialog = dialog;
                await dialog.ShowAsync();
                if (requested == null || token != activeRenderToken) return;
                var confirm = new ContentDialog { RequestedTheme = RequestedTheme, Title = L.Get("HistoryRestore"), Content = L.Get("HistoryRestoreConfirm"),
                    PrimaryButtonText = L.Get("HistoryRestore"), SecondaryButtonText = L.Get("Cancel") };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                if (token != activeRenderToken) return;
                // First commit pending edits so the rescue checkpoint remains
                // fully editable, even when the user restores an older version.
                if (HasUnsavedChanges() && !await SaveCurrentAnnotationsWithFeedbackAsync()) return;
                await RestoreSnapshotAndReloadAsync(requested);
            }
            catch (Exception ex) { await new Windows.UI.Popups.MessageDialog(L.Format("HistoryFailed", ex.Message), L.Get("ErrorTitle")).ShowAsync(); }
            finally { historyOpen = false; historyDialog = null; }
        }

        private async Task RestoreSnapshotAndReloadAsync(PdfSnapshot snapshot)
        {
            if (saveInProgress != null) await saveInProgress;
            var annotations = await GetAnnotationFolderAsync();
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(snapshot.Folder.Path)) != annotations.Path)
                throw new InvalidOperationException(L.Get("HistoryWrongDocument"));
            var file = currentFile;
            var index = pageIndex;
            var zoom = DocumentScroller.ZoomFactor;
            var wasFit = fitPageToViewport;
            var x = DocumentScroller.HorizontalOffset; var y = DocumentScroller.VerticalOffset;
            Exception failure = null;
            try
            {
                saveInProgress = RestoreSnapshotFilesAsync(snapshot);
                try { await saveInProgress; }
                catch (Exception ex) { failure = ex; }
                finally { saveInProgress = null; }
                if (failure == null || document == null)
                {
                    await LoadDocumentAsync(file);
                    if (document != null)
                    {
                        pageIndex = Math.Min(index, document.PageCount - 1);
                        fitPageToViewport = wasFit;
                        RequestZoomChange(x, y, zoom, true);
                    }
                }
            }
            finally { SetHistoryBusy(false); }
            if (failure != null) throw failure;
        }
        private void SetHistoryBusy(bool busy)
        {
            savingPdf = busy; MainCommandBar.IsEnabled = !busy;
            AnnotationToolbar.IsHitTestVisible = !busy; DocumentScroller.IsEnabled = !busy; UpdateDirtyState();
        }
        private async Task RestoreSnapshotFilesAsync(PdfSnapshot snapshot)
        {
            SetHistoryBusy(true); ClearInkSelection(); ClearTextSelection();
            var folder = await GetAnnotationFolderAsync();
            var original = currentFile;
            var rescue = await PdfSnapshotStore.PrepareAsync(original, folder, "rescue", pageIndex);
            await PdfSnapshotStore.PublishAsync(rescue);
            var succeeded = false;
            try
            {
                // Stop workers and release all mapped readers before replacing.
                ResetSearch(); ++activeRenderToken;
                if (textSource != null) await textSource.DisposeAsync();
                await renderGate.WaitAsync();
                try { document = null; ResetPageViews(); ResetThumbnails(); }
                finally { renderGate.Release(); }
                await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
                await PdfSnapshotStore.ApplyEditableAsync(await snapshot.Folder.GetFolderAsync("annotations"), folder);
                await (await snapshot.Folder.GetFileAsync("document.pdf")).CopyAndReplaceAsync(original);
                succeeded = true;
                try { await PdfSnapshotStore.PruneAsync(folder); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("History retention: " + ex.Message); }
            }
            finally
            {
                if (!succeeded)
                {
                    await PdfSnapshotStore.ApplyEditableAsync(await rescue.Folder.GetFolderAsync("annotations"), folder);
                    SetHistoryBusy(false);
                }
            }
        }
    }
}
