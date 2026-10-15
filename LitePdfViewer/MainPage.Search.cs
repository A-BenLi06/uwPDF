using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private int searchVersion;
        private bool searchBusy;
        private CancellationTokenSource searchCancellation = new CancellationTokenSource();
        private readonly List<SearchHit> searchHits = new List<SearchHit>();
        private SearchHit activeSearchHit, drawnSearchHit;
        private int searchHitIndex = -1, searchSkippedPages;
        private string indexedSearchQuery;
        private ulong indexedSearchToken;
        private bool searchIndexComplete;
        private Rect drawnSearchViewport;
        private Point drawnSearchOrigin, drawnSearchX, drawnSearchY;

        private sealed class SearchHit
        {
            public uint Page;
            public int First, Last;
            public List<TextQuad> Quads;
        }

        private int CancelSearchWork()
        {
            searchCancellation.Cancel();
            searchCancellation.Dispose();
            searchCancellation = new CancellationTokenSource();
            searchBusy = false;
            ClearSearchResults();
            return ++searchVersion;
        }

        private void ClearSearchResults()
        {
            if (IsSearchSelection()) ClearTextSelection();
            activeSearchHit = null;
            searchHits.Clear();
            searchHitIndex = -1;
            searchSkippedPages = 0;
            searchIndexComplete = false;
            indexedSearchQuery = null;
            UpdateSearchFocus();
            UpdateSearchNavigation();
        }

        private bool IsSearchSelection()
        {
            return activeSearchHit != null && selectedTextPage != null && SelectionEndpoint == selectedTextPage && selectedTextPage.Index == activeSearchHit.Page
                && selectionAnchor == activeSearchHit.First && selectionEnd == activeSearchHit.Last;
        }

        private void ShowSearch()
        {
            SearchPanel.Visibility = Visibility.Visible;
            UpdateAnnotationToolbarLayout(ReaderToolbar.ActualWidth);
            UpdateSearchFocus();
            UpdateSearchNavigation();
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
        }
        private void SearchButton_Click(object sender, RoutedEventArgs e) { ShowSearch(); }
        private void CloseSearch_Click(object sender, RoutedEventArgs e) { ResetSearch(); }
        private void ResetSearch()
        {
            CancelSearchWork();
            SearchPanel.Visibility = Visibility.Collapsed;
            UpdateAnnotationToolbarLayout(ReaderToolbar.ActualWidth);
            SearchTextBox.Text = string.Empty;
            SearchStatus.Text = string.Empty;
        }
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchStatus == null) return;
            var version = CancelSearchWork();
            SearchStatus.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(SearchTextBox.Text)) return;
            await Task.Delay(250);
            if (version == searchVersion && SearchPanel.Visibility == Visibility.Visible)
                await FindTextAsync(true, true);
        }
        private async void FindNext_Click(object sender, RoutedEventArgs e) { await FindTextAsync(true, false); }
        private async void FindPrevious_Click(object sender, RoutedEventArgs e) { await FindTextAsync(false, false); }
        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { ResetSearch(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                var backward = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                await FindTextAsync(!backward, false);
            }
        }

        private void UpdateSearchNavigation()
        {
            if (FindNextButton == null || FindPreviousButton == null) return;
            FindNextButton.IsEnabled = FindPreviousButton.IsEnabled = !searchBusy && searchIndexComplete && searchHits.Count > 0;
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
            searchBusy = true;
            UpdateSearchNavigation();
            try
            {
                if (restart || !searchIndexComplete || indexedSearchQuery != query || indexedSearchToken != token)
                {
                    ClearSearchResults();
                    SearchStatus.Text = L.Get("Searching");
                    var start = (int)pageIndex;
                    var pages = new List<SearchHit>[pageViews.Count];
                    var skipped = 0;
                    for (var step = 0; step < pageViews.Count; step++)
                    {
                        if (cancellation.IsCancellationRequested) return;
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
                        if (glyphs == null || version != searchVersion || token != activeRenderToken) return;
                        var matches = await MatchPageTextAsync(glyphs, query, view.Index, cancellation);
                        if (cancellation.IsCancellationRequested) return;
                        if (version != searchVersion || token != activeRenderToken) return;
                        if (matches == null) return;
                        pages[index] = matches;
                        // Show the first useful match while the remaining pages
                        // are counted. Retain ranges, not every page's glyphs/quads.
                        if (activeSearchHit == null && matches.Count > 0)
                        {
                            await ShowSearchMatchAsync(matches[forward ? 0 : matches.Count - 1], glyphs, version, token, cancellation);
                            if (version != searchVersion || token != activeRenderToken) return;
                        }
                        SearchStatus.Text = activeSearchHit == null
                            ? L.Format("SearchingPage", index + 1) : L.Get("CountingMatches");
                    }
                    if (version != searchVersion || token != activeRenderToken) return;
                    foreach (var matches in pages) if (matches != null) searchHits.AddRange(matches);
                    indexedSearchQuery = query;
                    indexedSearchToken = token;
                    searchIndexComplete = true;
                    searchSkippedPages = skipped;
                    searchHitIndex = activeSearchHit == null ? -1 : searchHits.IndexOf(activeSearchHit);
                }
                else if (searchHits.Count > 0)
                {
                    var next = (searchHitIndex + (forward ? 1 : -1) + searchHits.Count) % searchHits.Count;
                    var hit = searchHits[next];
                    var view = pageViews[(int)hit.Page];
                    var glyphs = view.Glyphs ?? await source.ReadPageAsync(hit.Page, cancellation);
                    if (glyphs == null || version != searchVersion || token != activeRenderToken) return;
                    await ShowSearchMatchAsync(hit, glyphs, version, token, cancellation);
                    if (version != searchVersion || token != activeRenderToken) return;
                    searchHitIndex = next;
                }
                SearchStatus.Text = searchHits.Count == 0
                    ? (searchSkippedPages > 0 ? L.Format("NoMatchesPartial", searchSkippedPages) : L.Get("NoMatches"))
                    : L.Format("MatchCount", searchHitIndex + 1, searchHits.Count)
                        + (searchSkippedPages > 0 ? L.Format("SkippedPages", searchSkippedPages) : "");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == searchVersion && token == activeRenderToken)
                {
                    ClearSearchResults();
                    SearchStatus.Text = L.Format("SearchFailed", ex.Message);
                }
            }
            finally
            {
                if (version == searchVersion) { searchBusy = false; UpdateSearchNavigation(); }
            }
        }

        private static Task<List<SearchHit>> MatchPageTextAsync(List<TextGlyph> glyphs, string query,
            uint page, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                var segments = new string[glyphs.Count];
                for (var g = 0; g < glyphs.Count; g++)
                {
                    if ((g & 255) == 0 && cancellation.IsCancellationRequested) return null;
                    segments[g] = glyphs[g].Text;
                }
                var results = new List<SearchHit>();
                var matches = PdfTextSearch.FindAll(segments, query, cancellation);
                if (matches == null) return null;
                foreach (var match in matches)
                {
                    if (cancellation.IsCancellationRequested) return null;
                    results.Add(new SearchHit { Page = page, First = match.First, Last = match.Last });
                }
                return results;
            });
        }

        private async Task ShowSearchMatchAsync(SearchHit hit, List<TextGlyph> glyphs,
            int version, ulong token, CancellationToken cancellation)
        {
            var view = pageViews[(int)hit.Page];
            if(!await TryEnsurePageGeometryAsync(view,token,cancellation)) return;
            if (version != searchVersion || token != activeRenderToken) return;
            var width = view.LayoutWidth; var height = view.LayoutHeight;
            var quads = await Task.Run(() => BuildTextQuads(glyphs, hit.First, hit.Last, width, height, cancellation));
            if (version != searchVersion || token != activeRenderToken) return;
            if (quads == null || cancellation.IsCancellationRequested) return;
            // Prepare the result without a preliminary page-top jump/refit.
            // The final match position is the only native scrolling request.
            pendingLayoutAnchor = null;
            ClearTextSelection();
            RealizePage(view);
            view.Glyphs = glyphs;
            selectedTextPage = view;
            selectionAnchor = hit.First;
            selectionEnd = hit.Last;
            if (activeSearchHit != null) activeSearchHit.Quads = null;
            hit.Quads = quads;
            activeSearchHit = hit;
            RedrawTextSelection(view);
            var y = pageTops[(int)view.Index] + glyphs[hit.First].Y * view.LayoutHeight;
            drawnSearchHit = null;
            UpdateSearchFocus();
            WakeRenderer();
            await ScrollToSearchMatchAsync(y * DocumentScroller.ZoomFactor - DocumentScroller.ViewportHeight * 0.25, cancellation);
        }

        private async Task ScrollToSearchMatchAsync(double target, CancellationToken cancellation)
        {
            // Community Toolkit SmoothScrollIntoView uses one animated ChangeView
            // and waits for the final ViewChanged. Equal/clamped targets must not wait.
            if(cancellation.IsCancellationRequested) return;
            target = Math.Max(0, Math.Min(DocumentScroller.ScrollableHeight, target));
            if (Math.Abs(target - DocumentScroller.VerticalOffset) < 0.5) return;
            var finished = new TaskCompletionSource<bool>();
            EventHandler<ScrollViewerViewChangedEventArgs> changed = (sender, args) =>
            {
                if (!args.IsIntermediate) finished.TrySetResult(true);
            };
            DocumentScroller.ViewChanged += changed;
            try
            {
                using (cancellation.Register(() => finished.TrySetResult(false)))
                {
                    if (!DocumentScroller.ChangeView(null, target, null, false)) return;
                    // A suspended view may not deliver a final event. Do not leave
                    // search navigation permanently disabled in that case.
                    await Task.WhenAny(finished.Task, Task.Delay(1500));
                    if(cancellation.IsCancellationRequested) return;
                }
            }
            finally { DocumentScroller.ViewChanged -= changed; }
        }

        private void UpdateSearchFocus()
        {
            if (SearchFocusLayer == null) return;
            var visible = activeSearchHit != null && SearchPanel.Visibility == Visibility.Visible;
            SearchFocusLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) { drawnSearchHit = null; SearchDimPath.Data = SearchMatchPath.Data = null; return; }
            var viewport = new Rect(0, 0, Math.Max(0, DocumentScroller.ViewportWidth), Math.Max(0, DocumentScroller.ViewportHeight));
            if (documentVerticalBar != null && VisualTreeHelper.GetParent(documentVerticalBar) == null)
                documentVerticalBar = FindNativeVerticalScrollBar(DocumentScroller);
            if (documentVerticalBar != null && documentVerticalBar.Visibility == Visibility.Visible)
                viewport.Width = Math.Max(0, Math.Min(viewport.Width,
                    documentVerticalBar.TransformToVisual(SearchFocusLayer).TransformPoint(new Point()).X));
            var view = pageViews[(int)activeSearchHit.Page];
            var origin = new Point(double.MinValue, double.MinValue);
            var x = new Point(); var y = new Point();
            GeneralTransform transform = null;
            if (view.Host != null)
            {
                transform = view.Host.TransformToVisual(SearchFocusLayer);
                origin = transform.TransformPoint(new Point());
                x = transform.TransformPoint(new Point(view.LayoutWidth, 0));
                y = transform.TransformPoint(new Point(0, view.LayoutHeight));
            }
            if (drawnSearchHit == activeSearchHit && drawnSearchViewport.Equals(viewport) && drawnSearchOrigin.Equals(origin)
                && drawnSearchX.Equals(x) && drawnSearchY.Equals(y)) return;
            drawnSearchHit = activeSearchHit; drawnSearchViewport = viewport;
            drawnSearchOrigin = origin; drawnSearchX = x; drawnSearchY = y;
            SearchFocusLayer.Clip = new RectangleGeometry { Rect = viewport };
            var mask = new PathGeometry { FillRule = FillRule.EvenOdd };
            mask.Figures.Add(SearchFigure(new[] { new Point(0, 0), new Point(viewport.Width, 0),
                new Point(viewport.Width, viewport.Height), new Point(0, viewport.Height) }));
            var highlight = new PathGeometry { FillRule = FillRule.Nonzero };
            if (transform != null)
            {
                foreach (var quad in activeSearchHit.Quads)
                {
                    var points = new Point[4]; var normalized = new[] { quad.A, quad.B, quad.D, quad.C };
                    for (int i = 0; i < 4; i++) points[i] = transform.TransformPoint(new Point(normalized[i].X * view.LayoutWidth, normalized[i].Y * view.LayoutHeight));
                    mask.Figures.Add(SearchFigure(points));
                    highlight.Figures.Add(SearchFigure(points));
                }
            }
            SearchDimPath.Data = mask;
            SearchMatchPath.Data = highlight;
        }

        private static PathFigure SearchFigure(Point[] points)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            for (int i = 1; i < points.Length; i++) figure.Segments.Add(new LineSegment { Point = points[i] });
            return figure;
        }
    }
}
