using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private int searchVersion;
        private bool searchBusy;
        private CancellationTokenSource searchCancellation = new CancellationTokenSource();

        private int CancelSearchWork()
        {
            searchCancellation.Cancel();
            searchCancellation.Dispose();
            searchCancellation = new CancellationTokenSource();
            searchBusy = false;
            return ++searchVersion;
        }

        private void ShowSearch()
        {
            SearchPanel.Visibility = Visibility.Visible;
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
        }
        private void SearchButton_Click(object sender, RoutedEventArgs e) { ShowSearch(); }
        private void CloseSearch_Click(object sender, RoutedEventArgs e) { ResetSearch(); }
        private void ResetSearch()
        {
            CancelSearchWork();
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchTextBox.Text = string.Empty;
            SearchStatus.Text = string.Empty;
        }
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchStatus == null) return;
            var version = CancelSearchWork();
            SearchStatus.Text = string.Empty;
            await Task.Delay(250);
            if (version == searchVersion && SearchPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(SearchTextBox.Text))
                await FindTextAsync(true, true);
        }
        private async void FindNext_Click(object sender, RoutedEventArgs e) { await FindTextAsync(true, false); }
        private async void FindPrevious_Click(object sender, RoutedEventArgs e) { await FindTextAsync(false, false); }
        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { ResetSearch(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await FindTextAsync(true, false); }
        }

        private async Task FindTextAsync(bool forward, bool restart)
        {
            if (document == null || textSource == null || searchBusy) return;
            var query = PdfTextSearch.NormalizeQuery(SearchTextBox.Text);
            if (query.Length == 0) return;
            var version = searchVersion;
            var token = activeRenderToken;
            var source = textSource;
            var cancellation = searchCancellation.Token;
            var skipped = 0;
            // Bringing a match near the top can leave the previous page's tail
            // visible. The leading-edge page indicator is a reading position,
            // not the search cursor: continue from the actual selection page.
            var continueSelection = !restart && selectedTextPage != null &&
                selectedTextPage.Index < pageViews.Count &&
                ReferenceEquals(pageViews[(int)selectedTextPage.Index], selectedTextPage) &&
                selectionAnchor >= 0 && selectionEnd >= 0;
            var start = continueSelection ? (int)selectedTextPage.Index : (int)pageIndex;
            var anchor = continueSelection
                ? (forward ? Math.Min(selectionAnchor, selectionEnd) + 1 : Math.Max(selectionAnchor, selectionEnd) - 1)
                : (forward ? 0 : int.MaxValue);
            searchBusy = true;
            SearchStatus.Text = "正在查找…";
            try
            {
                List<TextGlyph> firstGlyphs = null;
                int fallbackStart = -1, fallbackEnd = -1;
                for (var step = 0; step < pageViews.Count; step++)
                {
                    if (version != searchVersion || token != activeRenderToken) return;
                    var index = (start + (forward ? step : -step) + pageViews.Count) % pageViews.Count;
                    var view = pageViews[index];
                    List<TextGlyph> glyphs;
                    try { glyphs = view.Glyphs ?? await source.ReadPageAsync((uint)index, cancellation); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        if (source.IsUnavailable) throw;
                        skipped++;
                        System.Diagnostics.Debug.WriteLine("Search skipped page " + (index + 1) + ": " + ex.Message);
                        continue;
                    }
                    if (version != searchVersion || token != activeRenderToken) return;
                    var result = await MatchPageTextAsync(glyphs, query,
                        step == 0 ? anchor : forward ? 0 : int.MaxValue, forward, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (version != searchVersion || token != activeRenderToken) return;
                    var found = result.Found;
                    var wrapped = result.Wrapped;
                    if (step == 0 && wrapped.Found)
                    {
                        firstGlyphs = glyphs;
                        fallbackStart = wrapped.First;
                        fallbackEnd = wrapped.Last;
                    }
                    if (found.Found)
                    {
                        await ShowSearchMatchAsync(view, glyphs, found.First, found.Last, skipped, version, token, cancellation);
                        return;
                    }
                    SearchStatus.Text = "正在查找：第 " + (index + 1) + " 页";
                }
                if (fallbackStart >= 0) await ShowSearchMatchAsync(pageViews[start], firstGlyphs, fallbackStart, fallbackEnd, skipped, version, token, cancellation);
                else if (skipped > 0) SearchStatus.Text = "未找到匹配文字；" + skipped + " 页无法读取文字";
                else SearchStatus.Text = "没有匹配文字";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == searchVersion && token == activeRenderToken) SearchStatus.Text = "查找失败：" + ex.Message;
            }
            finally { if (version == searchVersion) searchBusy = false; }
        }

        private struct PageSearchResult
        {
            public PdfSearchMatch Found, Wrapped;
        }

        private static Task<PageSearchResult> MatchPageTextAsync(List<TextGlyph> glyphs, string query,
            int anchor, bool forward, CancellationToken cancellation)
        {
            // Cached pages still need to yield: normalization, glyph mapping and
            // matching must not monopolize input processing on the UI thread.
            return Task.Run(() =>
            {
                var segments = new string[glyphs.Count];
                for (var g = 0; g < glyphs.Count; g++)
                {
                    if ((g & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    segments[g] = glyphs[g].Text;
                }
                PdfSearchMatch wrapped;
                var found = PdfTextSearch.Find(segments, query, anchor, forward, out wrapped, cancellation);
                return new PageSearchResult { Found = found, Wrapped = wrapped };
            }, cancellation);
        }

        private async Task ShowSearchMatchAsync(PageView view, List<TextGlyph> glyphs, int first, int last, int skipped,
            int version, ulong token, CancellationToken cancellation)
        {
            await EnsurePageGeometryAsync(view, token, cancellation);
            if (version != searchVersion || token != activeRenderToken) return;
            GoToPage(view.Index);
            ClearTextSelection();
            RealizePage(view);
            view.Glyphs = glyphs;
            selectedTextPage = view;
            selectionAnchor = first;
            selectionEnd = last;
            RedrawTextSelection(view);
            // Bring the match into view at the existing zoom.
            var y = pageTops[(int)view.Index] + glyphs[first].Y * view.LayoutHeight;
            DocumentScroller.ChangeView(null, Math.Max(0, y * DocumentScroller.ZoomFactor - DocumentScroller.ViewportHeight * 0.25), null, true);
            SearchStatus.Text = "第 " + (view.Index + 1) + " 页" + (skipped > 0 ? "；" + skipped + " 页无法读取文字" : "");
        }
    }
}
