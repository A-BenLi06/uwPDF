using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private PdfTextSource textSource;
        private PageView selectedTextPage;
        private int selectionAnchor = -1, selectionEnd = -1;
        private uint? selectionPointer;
        private MenuFlyout textMenu;
        private bool selectionIsPen;
        private Point selectionLastPoint;
        private DispatcherTimer selectionScrollTimer;
        private Point selectionViewportPoint;
        private static readonly Windows.UI.Core.CoreCursor TextCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.IBeam, 0);
        private static readonly Windows.UI.Core.CoreCursor ArrowCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);

        private sealed class TextHighlight
        {
            public readonly List<Rect> Rectangles = new List<Rect>();
            public readonly List<TextQuad> Quads = new List<TextQuad>();
            public Color Color;
        }

        private struct TextQuad
        {
            public Point A, B, C, D; // top left/right, bottom left/right in page coordinates
        }

        private void TopSelectButton_Click(object sender, RoutedEventArgs e) { SetInkTool(InkTool.None); }

        private void AttachTextLayers(PageView view)
        {
            view.HighlightLayer = new Canvas { IsHitTestVisible = false };
            view.SelectionLayer = new Canvas { Background = new SolidColorBrush(Colors.Transparent),
                IsHitTestVisible = currentInkTool == InkTool.None };
            // Highlights sit above the bitmap; ink and selection retain their own input modes.
            view.Host.Children.Insert(1, view.HighlightLayer);
            view.Host.Children.Add(view.SelectionLayer);
            view.SelectionLayer.PointerPressed += (s, e) => TextPointerPressed(view, e);
            view.SelectionLayer.PointerMoved += (s, e) => TextPointerMoved(view, e);
            view.SelectionLayer.PointerReleased += (s, e) => EndTextDrag(view, e);
            view.SelectionLayer.PointerCanceled += (s, e) => EndTextDrag(view, e, true);
            view.SelectionLayer.PointerCaptureLost += (s, e) => { if (inkGesturePointer == e.Pointer.PointerId) CancelInkGesture(); selectionPointer = null; StopSelectionScroll(); };
            view.SelectionLayer.PointerExited += (s, e) => SetTextCursor(false);
            view.SelectionLayer.RightTapped += (s, e) => ShowTextMenu(view, e);
            view.SelectionLayer.DoubleTapped += (s, e) =>
            {
                var hit = HitGlyph(view, e.GetPosition(view.SelectionLayer), false);
                if (hit < 0) return;
                SelectWord(view, hit);
                e.Handled = true;
            };
            RedrawTextLayers(view);
        }

        private Task EnsurePageTextAsync(PageView view, ulong token)
        {
            if (view.TextTask != null) return view.TextTask;
            if (view.Glyphs != null || view.TextFailed || textSource == null) return Task.FromResult(0);
            view.TextTask = LoadPageTextAsync(view, token);
            return view.TextTask;
        }

        private async Task LoadPageTextAsync(PageView view, ulong token)
        {
            try
            {
                var source = textSource;
                var glyphs = await source.ReadPageAsync(view.Index);
                if (source != textSource) return;
                if (token != activeRenderToken) return;
                // Evicted text is discarded too, so long documents keep a bounded text cache.
                if (!view.IsRealized) return;
                view.Glyphs = glyphs;
                AutomationProperties.SetHelpText(view.Chrome, glyphs.Count == 0 ? L.Get("NoSelectableText") : L.Get("SelectTextHelp"));
            }
            catch (Exception)
            {
                if (token == activeRenderToken && view.IsRealized)
                {
                    view.TextFailed = true;
                    AutomationProperties.SetHelpText(view.Chrome, L.Get("TextLayerUnavailable"));
                }
            }
            finally { view.TextTask = null; }
        }

        private async void TextPointerPressed(PageView view, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(view.SelectionLayer);
            if (currentInkTool != InkTool.None || point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch) return;
            if (point.Properties.IsBarrelButtonPressed)
            {
                if (!view.InkLoaded || view.IsLoadingInk) return;
                if (view.SelectionLayer.CapturePointer(e.Pointer))
                { BeginInkGesture(view, point.Position, e.Pointer.PointerId); e.Handled = true; }
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            ClearInkSelection(); ClearTextSelection();
            if (!view.SelectionLayer.CapturePointer(e.Pointer)) return;
            selectedTextPage = view;
            selectionPointer = e.Pointer.PointerId;
            selectionIsPen = point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen;
            selectionLastPoint = point.Position;
            var start = point.Position; var token = activeRenderToken;
            e.Handled = true;
            if (view.Glyphs == null) await EnsurePageTextAsync(view, token);
            if (token != activeRenderToken || selectedTextPage != view || view.SelectionLayer == null) return;
            var hit = HitGlyph(view, start, selectionIsPen, selectionIsPen ? 8 / DocumentScroller.ZoomFactor : 0);
            if (hit < 0) { ClearTextSelection(); return; }
            selectionAnchor = hit;
            selectionEnd = HitGlyph(view, selectionLastPoint, true);
            if (selectionEnd < 0) selectionEnd = hit;
            Focus(FocusState.Programmatic); RedrawTextSelection(view);
        }

        private void TextPointerMoved(PageView view, PointerRoutedEventArgs e)
        {
            if (selectedInkPage == view && inkGesturePointer == e.Pointer.PointerId)
            { UpdateInkGesture(view, e.GetCurrentPoint(view.SelectionLayer).Position, e.Pointer.PointerId); e.Handled = true; return; }
            if (selectedTextPage != view || selectionPointer != e.Pointer.PointerId)
            {
                if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
                    SetTextCursor(HitGlyph(view, e.GetCurrentPoint(view.SelectionLayer).Position, false) >= 0);
                return;
            }
            selectionLastPoint = e.GetCurrentPoint(view.SelectionLayer).Position;
            selectionViewportPoint = e.GetCurrentPoint(DocumentScroller).Position;
            UpdateSelectionEnd(view, selectionLastPoint);
            UpdateSelectionScroll();
            e.Handled = true;
        }
        private void UpdateSelectionEnd(PageView view, Point point)
        {
            if (selectionAnchor < 0) return;
            var hit = HitGlyph(view, point, true);
            // A small screen-space dead band keeps a pen grazing the space
            // between lines from flipping the endpoint back and forth.
            if (selectionIsPen && hit >= 0 && selectionEnd >= 0 && hit != selectionEnd)
            {
                var old = view.Glyphs[selectionEnd]; var next = view.Glyphs[hit];
                double line, gap, height;
                GlyphSpacing(view, old, next, out line, out gap, out height);
                var margin = 3 / DocumentScroller.ZoomFactor;
                var insideNext = ContainsQuad(new TextQuad { A = next.TopLeft, B = next.TopRight, C = next.BottomLeft, D = next.BottomRight }, new Point(point.X / view.LayoutWidth, point.Y / view.LayoutHeight));
                if (!insideNext && line > height * 0.6 && point.Y >= old.Y * view.LayoutHeight - margin &&
                    point.Y <= (old.Y + old.Height) * view.LayoutHeight + margin) hit = selectionEnd;
            }
            if (hit >= 0 && hit != selectionEnd) { selectionEnd = hit; RedrawTextSelection(view); }
        }
        private void UpdateSelectionScroll()
        {
            if (selectionScrollTimer == null)
            {
                selectionScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                selectionScrollTimer.Tick += (s, e) =>
                {
                    var view = selectedTextPage;
                    if (selectionPointer == null || view == null || view.SelectionLayer == null) { StopSelectionScroll(); return; }
                    var height = DocumentScroller.ViewportHeight;
                    var speed = selectionViewportPoint.Y < 28 ? -Math.Min(14, (28 - selectionViewportPoint.Y) * 0.35) :
                        selectionViewportPoint.Y > height - 28 ? Math.Min(14, (selectionViewportPoint.Y - height + 28) * 0.35) : 0;
                    if (speed == 0) return;
                    var before = DocumentScroller.VerticalOffset;
                    DocumentScroller.ChangeView(null, Math.Max(0, Math.Min(DocumentScroller.ScrollableHeight, before + speed)), null, true);
                    var p = DocumentScroller.TransformToVisual(view.SelectionLayer).TransformPoint(selectionViewportPoint);
                    selectionLastPoint = p; UpdateSelectionEnd(view, p);
                };
            }
            if (!selectionScrollTimer.IsEnabled) selectionScrollTimer.Start();
        }
        private void StopSelectionScroll() { if (selectionScrollTimer != null) selectionScrollTimer.Stop(); }

        private static void SetTextCursor(bool overText)
        {
            var cursor = overText ? TextCursor : ArrowCursor;
            if (Window.Current.CoreWindow.PointerCursor != cursor) Window.Current.CoreWindow.PointerCursor = cursor;
        }

        private void EndTextDrag(PageView view, PointerRoutedEventArgs e, bool canceled = false)
        {
            if (selectedInkPage == view && inkGesturePointer == e.Pointer.PointerId)
            {
                if (canceled) CancelInkGesture();
                else FinishInkGesture(view, e.GetCurrentPoint(view.SelectionLayer).Position, e.Pointer.PointerId);
                view.SelectionLayer.ReleasePointerCapture(e.Pointer); e.Handled = true; return;
            }
            if (selectionPointer != e.Pointer.PointerId) return;
            if (canceled) { ClearTextSelection(); e.Handled = true; return; }
            selectionPointer = null; StopSelectionScroll();
            view.SelectionLayer.ReleasePointerCapture(e.Pointer);
            RedrawTextSelection(view);
            e.Handled = true;
        }

        private static int HitGlyph(PageView view, Point point, bool nearest, double maximumDistance = double.PositiveInfinity)
        {
            if (view.Glyphs == null) return -1;
            var best = -1; var distance = double.MaxValue;
            for (var i = 0; i < view.Glyphs.Count; i++)
            {
                var g = view.Glyphs[i];
                if (string.IsNullOrWhiteSpace(g.Text) || g.Width <= 0 || g.Height <= 0) continue;
                var ax = Math.Max(g.X * view.LayoutWidth - point.X, Math.Max(0, point.X - (g.X + g.Width) * view.LayoutWidth));
                var ay = Math.Max(g.Y * view.LayoutHeight - point.Y, Math.Max(0, point.Y - (g.Y + g.Height) * view.LayoutHeight));
                if (ax * ax + ay * ay > Math.Min(distance, maximumDistance * maximumDistance)) continue;
                var q = new TextQuad { A = g.TopLeft, B = g.TopRight, C = g.BottomLeft, D = g.BottomRight };
                if (ContainsQuad(q, new Point(point.X / view.LayoutWidth, point.Y / view.LayoutHeight))) return i;
                if (!nearest) continue;
                var d = double.MaxValue;
                for (var edge = 0; edge < 4; edge++)
                {
                    var p1 = QuadPoint(q, edge); var p2 = QuadPoint(q, (edge + 1) % 4);
                    var a = new Point(p1.X * view.LayoutWidth, p1.Y * view.LayoutHeight);
                    var b = new Point(p2.X * view.LayoutWidth, p2.Y * view.LayoutHeight);
                    var dx = b.X - a.X; var dy = b.Y - a.Y;
                    var length = dx * dx + dy * dy;
                    var t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length));
                    var px = point.X - a.X - t * dx; var py = point.Y - a.Y - t * dy;
                    d = Math.Min(d, px * px + py * py);
                }
                if (d <= maximumDistance * maximumDistance && d < distance) { best = i; distance = d; }
            }
            return best;
        }

        private void SelectWord(PageView view, int hit)
        {
            ClearTextSelection();
            selectedTextPage = view;
            selectionAnchor = selectionEnd = hit;
            while (selectionAnchor > 0 && WordAdjacent(view, view.Glyphs[selectionAnchor - 1], view.Glyphs[selectionAnchor])) selectionAnchor--;
            while (selectionEnd + 1 < view.Glyphs.Count && WordAdjacent(view, view.Glyphs[selectionEnd], view.Glyphs[selectionEnd + 1])) selectionEnd++;
            RedrawTextLayers(view);
        }

        private static bool WordAdjacent(PageView view, TextGlyph a, TextGlyph b)
        {
            double lineOffset, gap, height;
            GlyphSpacing(view, a, b, out lineOffset, out gap, out height);
            return !string.IsNullOrWhiteSpace(a.Text) && !string.IsNullOrWhiteSpace(b.Text) &&
                lineOffset < height * 0.4 && Math.Abs(gap) < height * 0.25;
        }

        private static void GlyphSpacing(PageView view, TextGlyph a, TextGlyph b, out double lineOffset, out double gap, out double height)
        {
            var cos = Math.Cos(a.Angle); var sin = Math.Sin(a.Angle);
            var dx = (b.X - a.X) * view.LayoutWidth;
            var dy = (b.Y - a.Y) * view.LayoutHeight;
            lineOffset = Math.Abs(-sin * dx + cos * dy);
            gap = Math.Abs(cos * dx + sin * dy) - (Math.Abs(cos) * a.Width * view.LayoutWidth + Math.Abs(sin) * a.Height * view.LayoutHeight);
            height = Math.Max(Math.Abs(sin) * a.Width * view.LayoutWidth + Math.Abs(cos) * a.Height * view.LayoutHeight,
                Math.Abs(sin) * b.Width * view.LayoutWidth + Math.Abs(cos) * b.Height * view.LayoutHeight);
        }

        private void ClearTextSelection()
        {
            if (textMenu != null) { textMenu.Hide(); textMenu = null; }
            var old = selectedTextPage;
            if (old != null && old.SelectionLayer != null) old.SelectionLayer.ReleasePointerCaptures();
            selectedTextPage = null;
            selectionPointer = null;
            selectionAnchor = selectionEnd = -1;
            StopSelectionScroll();
            if (old != null && old.SelectionLayer != null) old.SelectionLayer.Children.Clear();
        }

        private string SelectedText()
        {
            if (selectedTextPage == null || selectedTextPage.Glyphs == null || selectionAnchor < 0) return string.Empty;
            var text = new StringBuilder();
            TextGlyph previous = null;
            for (var i = Math.Min(selectionAnchor, selectionEnd); i <= Math.Max(selectionAnchor, selectionEnd); i++)
            {
                var g = selectedTextPage.Glyphs[i];
                if (previous != null && !string.IsNullOrWhiteSpace(g.Text) && !string.IsNullOrWhiteSpace(previous.Text))
                {
                    double lineOffset, gap, height;
                    GlyphSpacing(selectedTextPage, previous, g, out lineOffset, out gap, out height);
                    if (lineOffset > height * 0.6) text.AppendLine();
                    else if (gap > height * 0.2 &&
                        !string.IsNullOrWhiteSpace(g.Text) && !string.IsNullOrWhiteSpace(previous.Text)) text.Append(' ');
                }
                text.Append(g.Text);
                previous = g;
            }
            return text.ToString();
        }

        private void CopySelectedText()
        {
            var text = SelectedText();
            if (string.IsNullOrEmpty(text)) return;
            var content = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            content.SetText(text);
            try { Clipboard.SetContent(content); }
            catch (Exception) { ToolTipService.SetToolTip(selectedTextPage.Chrome, L.Get("CopyFailed")); }
        }

        private void SelectAllPageText(PageView view)
        {
            if (view.Glyphs == null || view.Glyphs.Count == 0) return;
            ClearTextSelection();
            selectedTextPage = view;
            selectionAnchor = 0;
            selectionEnd = view.Glyphs.Count - 1;
            RedrawTextLayers(view);
        }

        private void ShowTextMenu(PageView view, RightTappedRoutedEventArgs e)
        {
            if (currentInkTool != InkTool.None || selectedInkPage == view) return;
            var point = e.GetPosition(view.SelectionLayer);
            var hit = HitGlyph(view, point, false);
            if (hit >= 0 && (selectedTextPage != view || hit < Math.Min(selectionAnchor, selectionEnd) || hit > Math.Max(selectionAnchor, selectionEnd))) SelectWord(view, hit);
            if (selectedTextPage != null && selectedTextPage != view) ClearTextSelection();
            var hasSelection = selectedTextPage == view && selectionAnchor >= 0;
            var menu = new MenuFlyout();
            AddTextCommand(menu, L.Get("CopyCommand"), hasSelection, CopySelectedText);
            AddTextCommand(menu, L.Get("HighlightSelection"), hasSelection && view.HighlightsLoaded, () => HighlightSelection(view));
            AddTextCommand(menu, L.Get("SelectAllPage"), view.Glyphs != null && view.Glyphs.Count > 0, () => SelectAllPageText(view));
            var highlight = FindHighlight(view, point);
            if (highlight != null)
            {
                AddTextCommand(menu, L.Get("DeleteHighlight"), true, () =>
                {
                    view.Highlights.Remove(highlight);
                    view.UndoKinds.Clear(); view.InkMoves.Clear();
                    MarkPageDirty(view);
                    RedrawTextLayers(view);
                });
            }
            AddTextCommand(menu, L.Get("Deselect"), hasSelection, ClearTextSelection);
            if (view.Glyphs == null || view.Glyphs.Count == 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(new MenuFlyoutItem { Text = view.TextFailed ? L.Get("TextLayerFailed") :
                    view.Glyphs == null ? L.Get("LoadingText") : L.Get("NoSelectableText"), IsEnabled = false });
            }
            if (textMenu != null) textMenu.Hide();
            textMenu = menu;
            menu.ShowAt(view.SelectionLayer, point);
            e.Handled = true;
        }

        private static void AddTextCommand(MenuFlyout menu, string label, bool enabled, Action action)
        {
            var command = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
            command.Click += (s, e) => action();
            menu.Items.Add(command);
        }

        private static TextHighlight FindHighlight(PageView view, Point point)
        {
            var normalized = new Point(point.X / view.LayoutWidth, point.Y / view.LayoutHeight);
            for (var i = view.Highlights.Count - 1; i >= 0; i--)
            {
                foreach (var quad in view.Highlights[i].Quads)
                    if (ContainsQuad(quad, normalized)) return view.Highlights[i];
                foreach (var rect in view.Highlights[i].Rectangles)
                    if (rect.Contains(normalized)) return view.Highlights[i];
            }
            return null;
        }

        private static Point QuadPoint(TextQuad q, int i) { return i == 0 ? q.A : i == 1 ? q.B : i == 2 ? q.D : q.C; }

        private static bool ContainsQuad(TextQuad q, Point p)
        {
            double sign = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = QuadPoint(q, i); var b = QuadPoint(q, (i + 1) % 4);
                var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (Math.Abs(cross) < 1e-12) continue;
                if (sign != 0 && sign * cross < 0) return false;
                sign = cross;
            }
            return sign != 0;
        }

        private List<TextQuad> SelectionQuads(PageView view)
        {
            if (selectedTextPage != view || view.Glyphs == null || selectionAnchor < 0 || selectionEnd < 0) return new List<TextQuad>();
            return BuildTextQuads(view.Glyphs, Math.Min(selectionAnchor, selectionEnd), Math.Max(selectionAnchor, selectionEnd),
                view.LayoutWidth, view.LayoutHeight);
        }

        private static List<TextQuad> BuildTextQuads(List<TextGlyph> glyphs, int first, int end, double width, double height,
            System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken))
        {
            var result = new List<TextQuad>();
            for (var i = first; i <= end; i++)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                var g = glyphs[i];
                if (g.Text == "\n" || g.Width <= 0 || g.Height <= 0) continue;
                var next = new TextQuad { A = g.TopLeft, B = g.TopRight, C = g.BottomLeft, D = g.BottomRight };
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    var quadHeight = Math.Sqrt(Math.Pow((next.C.X - next.A.X) * width, 2) + Math.Pow((next.C.Y - next.A.Y) * height, 2));
                    var gapTop = Math.Sqrt(Math.Pow((last.B.X - next.A.X) * width, 2) + Math.Pow((last.B.Y - next.A.Y) * height, 2));
                    var gapBottom = Math.Sqrt(Math.Pow((last.D.X - next.C.X) * width, 2) + Math.Pow((last.D.Y - next.C.Y) * height, 2));
                    if (quadHeight > 0 && gapTop < quadHeight * 0.35 && gapBottom < quadHeight * 0.35)
                    {
                        last.B = next.B; last.D = next.D;
                        result[result.Count - 1] = last;
                        continue;
                    }
                }
                result.Add(next);
            }
            return result;
        }

        private List<Rect> SelectionRectangles(PageView view)
        {
            var result = new List<Rect>();
            if (selectedTextPage != view || view.Glyphs == null) return result;
            for (var i = Math.Min(selectionAnchor, selectionEnd); i <= Math.Max(selectionAnchor, selectionEnd); i++)
            {
                var g = view.Glyphs[i];
                if (g.Width <= 0 || g.Height <= 0) continue;
                var rect = new Rect(g.X, g.Y, g.Width, g.Height);
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    if (Math.Abs(last.Y - rect.Y) < rect.Height * 0.15 && Math.Abs(last.Height - rect.Height) < rect.Height * 0.2 &&
                        rect.X >= last.X && rect.X - last.Right < rect.Height * 0.3)
                    {
                        last.Union(rect);
                        result[result.Count - 1] = last;
                        continue;
                    }
                }
                result.Add(rect);
            }
            return result;
        }

        private void HighlightSelection(PageView view)
        {
            var quads = SelectionQuads(view);
            if (quads.Count == 0 || !view.HighlightsLoaded) return;
            var highlight = new TextHighlight { Color = highlighterColor };
            highlight.Quads.AddRange(quads);
            view.Highlights.Add(highlight);
            view.UndoKinds.Add(1);
            MarkPageDirty(view);
            ClearTextSelection();
            RedrawTextLayers(view);
        }

        private void RedrawTextLayers(PageView view)
        {
            if (view.HighlightLayer == null) return;
            var pageBounds = new Rect(0, 0, view.LayoutWidth, view.LayoutHeight);
            view.HighlightLayer.Clip = new RectangleGeometry { Rect = pageBounds };
            view.SelectionLayer.Clip = new RectangleGeometry { Rect = pageBounds };
            view.HighlightLayer.Children.Clear();
            foreach (var highlight in view.Highlights)
            {
                PaintRectangles(view.HighlightLayer, view, highlight.Rectangles, highlight.Color, 0.35);
                PaintQuads(view.HighlightLayer, view, highlight.Quads, highlight.Color, 0.35);
            }
            RedrawTextSelection(view);
            if (activeSearchHit != null && activeSearchHit.Page == view.Index) UpdateSearchFocus();
        }

        private void RedrawTextSelection(PageView view)
        {
            if (view.SelectionLayer == null) return;
            view.SelectionLayer.Children.Clear();
            if (IsSearchSelection()) return;
            PaintQuads(view.SelectionLayer, view, SelectionQuads(view), Color.FromArgb(255, 0, 120, 215), 0.3);
            if (selectedTextPage == view && view.Glyphs != null && selectionAnchor >= 0 && selectionPointer == null)
            {
                var first = Math.Min(selectionAnchor, selectionEnd); var last = Math.Max(selectionAnchor, selectionEnd);
                AddTextHandle(view, view.Glyphs[first].BottomLeft, first, last);
                AddTextHandle(view, view.Glyphs[last].BottomRight, last, first);
            }
        }

        private static void PaintQuads(Canvas canvas, PageView view, List<TextQuad> quads, Color color, double opacity)
        {
            var brush = new SolidColorBrush(color);
            foreach (var q in quads)
            {
                var points = new PointCollection();
                foreach (var p in new[] { q.A, q.B, q.D, q.C }) points.Add(new Point(p.X * view.LayoutWidth, p.Y * view.LayoutHeight));
                canvas.Children.Add(new Polygon { Points = points, Fill = brush, Opacity = opacity, IsHitTestVisible = false });
            }
        }

        private static void PaintRectangles(Canvas canvas, PageView view, List<Rect> rectangles, Color color, double opacity)
        {
            var brush = new SolidColorBrush(color);
            foreach (var rect in rectangles)
            {
                var shape = new Rectangle { Width = rect.Width * view.LayoutWidth, Height = rect.Height * view.LayoutHeight,
                    Fill = brush, Opacity = opacity, IsHitTestVisible = false };
                Canvas.SetLeft(shape, rect.X * view.LayoutWidth);
                Canvas.SetTop(shape, rect.Y * view.LayoutHeight);
                canvas.Children.Add(shape);
            }
        }

        private async Task LoadTextHighlightsAsync(PageView view, StorageFolder folder, ulong token)
        {
            if (view.HighlightsLoaded) return;
            try
            {
                var file = await folder.TryGetItemAsync(HighlightFileName(view.Index)) as StorageFile;
                if (token != activeRenderToken) return;
                if (file == null)
                {
                    view.HighlightsLoaded = true;
                    return;
                }
                var json = JsonArray.Parse(await FileIO.ReadTextAsync(file));
                if (token != activeRenderToken) return;
                var loaded = new List<TextHighlight>();
                foreach (var item in json)
                {
                    var obj = item.GetObject();
                    var highlight = new TextHighlight { Color = ParseHexColor(obj.GetNamedString("color")) };
                    foreach (var value in obj.GetNamedArray("rectangles", new JsonArray()))
                    {
                        var r = value.GetArray();
                        if (r.Count != 4) continue;
                        var x = r[0].GetNumber(); var y = r[1].GetNumber();
                        var w = r[2].GetNumber(); var h = r[3].GetNumber();
                        if (x >= 0 && y >= 0 && w > 0 && h > 0 && x + w <= 1.01 && y + h <= 1.01)
                            highlight.Rectangles.Add(new Rect(x, y, w, h));
                    }
                    foreach (var value in obj.GetNamedArray("quads", new JsonArray()))
                    {
                        var q = value.GetArray();
                        if (q.Count != 8) continue;
                        var valid = true;
                        // Cropped characters can legitimately straddle a page edge.
                        // Keep their geometry; painting clips to the visible page.
                        foreach (var n in q)
                        {
                            var coordinate = n.GetNumber();
                            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate) || coordinate < -100 || coordinate > 100) valid = false;
                        }
                        if (!valid) continue;
                        highlight.Quads.Add(new TextQuad { A = new Point(q[0].GetNumber(), q[1].GetNumber()), B = new Point(q[2].GetNumber(), q[3].GetNumber()),
                            C = new Point(q[4].GetNumber(), q[5].GetNumber()), D = new Point(q[6].GetNumber(), q[7].GetNumber()) });
                    }
                    if (highlight.Rectangles.Count + highlight.Quads.Count > 0) loaded.Add(highlight);
                }
                view.Highlights.AddRange(loaded);
            }
            catch (System.IO.FileNotFoundException) { }
            // Preserve unreadable storage rather than silently replacing it on Save.
            if (token == activeRenderToken)
            {
                view.HighlightsLoaded = true;
                RedrawTextLayers(view);
            }
        }

        private static string HighlightFileName(uint index) { return "page-" + index.ToString(CultureInfo.InvariantCulture) + "-highlights.json"; }

        private static string SerializeHighlights(PageView view)
        {
            var annotations = new JsonArray();
            foreach (var highlight in view.Highlights)
            {
                var obj = new JsonObject();
                var c = highlight.Color;
                obj["color"] = JsonValue.CreateStringValue(string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B));
                var rects = new JsonArray();
                foreach (var r in highlight.Rectangles)
                {
                    var values = new JsonArray();
                    foreach (var number in new[] { r.X, r.Y, r.Width, r.Height }) values.Add(JsonValue.CreateNumberValue(number));
                    rects.Add(values);
                }
                obj["rectangles"] = rects;
                var quads = new JsonArray();
                foreach (var q in highlight.Quads)
                {
                    var values = new JsonArray();
                    foreach (var n in new[] { q.A.X, q.A.Y, q.B.X, q.B.Y, q.C.X, q.C.Y, q.D.X, q.D.Y }) values.Add(JsonValue.CreateNumberValue(n));
                    quads.Add(values);
                }
                obj["quads"] = quads;
                annotations.Add(obj);
            }
            return annotations.Stringify();
        }

        private async Task SavePageAnnotationsAsync(PageView view, bool clearDirty = true)
        {
            if (view.IsLoadingInk) return;
            var revision = view.Revision;
            var highlights = view.HighlightsLoaded ? SerializeHighlights(view) : null;
            var token = activeRenderToken;
            var folder = await GetAnnotationFolderAsync();
            if (token != activeRenderToken) return;
            await SaveInkAsync(view, folder);
            if (highlights != null)
            {
                var file = await folder.CreateFileAsync(HighlightFileName(view.Index), CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, highlights);
            }
            if (clearDirty && token == activeRenderToken && revision == view.Revision) ClearPageDirty(view);
        }
    }
}
