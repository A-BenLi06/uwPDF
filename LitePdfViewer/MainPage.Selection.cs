using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private PageView selectionEndPage;
        private int textSelectionVersion;
        private CancellationTokenSource selectionCancellation;
        private bool selectionUpdateBusy, selectionDragPending;
        private DateTime selectionLastTick, selectionEdgeSince;
        private int selectionScrollDirection;
        private PageView SelectionEndpoint { get { return selectionEndPage ?? selectedTextPage; } }

        private bool TryGetSelectionRange(PageView view, out int first, out int last)
        {
            first = last = -1;
            var endpoint = SelectionEndpoint;
            if (selectedTextPage == null || endpoint == null || selectionAnchor < 0 || selectionEnd < 0 || view.Glyphs == null) return false;
            var forward = selectedTextPage.Index < endpoint.Index || (selectedTextPage == endpoint && selectionAnchor <= selectionEnd);
            var low = forward ? selectedTextPage : endpoint;
            var high = forward ? endpoint : selectedTextPage;
            if (view.Index < low.Index || view.Index > high.Index) return false;
            first = view == low ? (forward ? selectionAnchor : selectionEnd) : 0;
            last = view == high ? (forward ? selectionEnd : selectionAnchor) : view.Glyphs.Count - 1;
            first = Math.Max(0, first); last = Math.Min(view.Glyphs.Count - 1, last);
            return first <= last;
        }

        private bool IsTextSelectionEndpoint(PageView view)
        {
            return selectedTextPage == view || (selectedTextPage != null && SelectionEndpoint == view);
        }

        private void RedrawSelectionPages()
        {
            foreach (var index in realizedPages) RedrawTextSelection(pageViews[index]);
        }

        private void InitializeTextSelectionInput()
        {
            DocumentScroller.PointerMoved += (s,e) =>
            {
                if (selectionPointer != e.Pointer.PointerId) return;
                selectionViewportPoint = e.GetCurrentPoint(DocumentScroller).Position;
                selectionDragPending = true; UpdateSelectionScroll(); e.Handled = true;
            };
            DocumentScroller.PointerReleased += (s,e) => { if (selectionPointer == e.Pointer.PointerId) EndTextDrag(selectedTextPage,e); };
            DocumentScroller.PointerCanceled += (s,e) => { if (selectionPointer == e.Pointer.PointerId) EndTextDrag(selectedTextPage,e,true); };
            DocumentScroller.PointerCaptureLost += (s,e) =>
            {
                if (selectionPointer != e.Pointer.PointerId) return;
                selectionPointer = null; StopSelectionScroll(); RedrawSelectionPages();
            };
        }

        private bool CaptureTextPointer(PointerRoutedEventArgs e)
        {
            if (!DocumentScroller.CapturePointer(e.Pointer)) return false;
            selectionPointer = e.Pointer.PointerId;
            selectionViewportPoint = e.GetCurrentPoint(DocumentScroller).Position;
            selectionDragPending = true;
            selectionLastTick = DateTime.UtcNow;
            selectionEdgeSince = DateTime.MinValue; selectionScrollDirection = 0;
            UpdateSelectionScroll();
            return true;
        }

        private async Task EnsureSelectionTextAsync(PageView view, int version, ulong token)
        {
            if (view.Glyphs != null || textSource == null) return;
            if (selectionCancellation == null) selectionCancellation = new CancellationTokenSource();
            var cancellation = selectionCancellation.Token;
            var source = textSource;
            var glyphs = await source.ReadPageAsync(view.Index,cancellation);
            if (glyphs == null || cancellation.IsCancellationRequested || version != textSelectionVersion || token != activeRenderToken || source != textSource) return;
            view.Glyphs = glyphs;
        }

        private async Task UpdateSelectionFromViewportAsync(Point viewport, int version, ulong token, bool final = false)
        {
            if (selectedTextPage == null || selectionAnchor < 0 || pageViews.Count == 0) return;
            viewport.X = Math.Max(0,Math.Min(DocumentScroller.ViewportWidth,viewport.X));
            viewport.Y = Math.Max(0,Math.Min(DocumentScroller.ViewportHeight,viewport.Y));
            var content = DocumentScroller.TransformToVisual(PageStack).TransformPoint(viewport);
            var index = PageAtOffset(Math.Max(0,content.Y));
            var view = pageViews[index];
            await EnsureSelectionTextAsync(view,version,token);
            if (version != textSelectionVersion || token != activeRenderToken || (!final && selectionPointer == null) || view.Glyphs == null) return;
            if(!final && selectionDragPending)
            {
                if(!view.IsRealized && !IsTextSelectionEndpoint(view)) view.Glyphs=null;
                return;
            }
            var point = new Point(content.X-PageBorderThickness,content.Y-pageTops[index]-PageBorderThickness);
            var old = SelectionEndpoint;
            UpdateSelectionEnd(view,point);
            if(old!=null && old!=SelectionEndpoint && old!=selectedTextPage && !old.IsRealized) old.Glyphs=null;
        }

        private async void SelectionScrollTick(object sender, object args)
        {
            if (selectionPointer == null || selectedTextPage == null) { StopSelectionScroll(); return; }
            var now = DateTime.UtcNow;
            var direction = PdfSelectionScroll.Direction(selectionViewportPoint.Y,DocumentScroller.ViewportHeight);
            if (direction != selectionScrollDirection)
            { selectionScrollDirection = direction; selectionEdgeSince = direction == 0 ? DateTime.MinValue : now; }
            var elapsed = Math.Min(.06,Math.Max(.001,(now-selectionLastTick).TotalSeconds));
            selectionLastTick = now;
            var dwell = selectionEdgeSince == DateTime.MinValue ? 0 : (now-selectionEdgeSince).TotalSeconds;
            var speed = PdfSelectionScroll.Speed(selectionViewportPoint.Y,DocumentScroller.ViewportHeight,dwell);
            if (speed != 0)
            {
                var offset = Math.Max(0,Math.Min(DocumentScroller.ScrollableHeight,DocumentScroller.VerticalOffset+speed*elapsed));
                if (Math.Abs(offset-DocumentScroller.VerticalOffset)>.1)
                {
                    pendingLayoutAnchor = null;
                    scrollActivity.NoteWheel(now);
                    DocumentScroller.ChangeView(null,offset,null,true);
                    selectionDragPending = true;
                }
            }
            if (selectionUpdateBusy || !selectionDragPending) return;
            selectionDragPending = false; selectionUpdateBusy = true;
            var version = textSelectionVersion; var token = activeRenderToken;
            try { await UpdateSelectionFromViewportAsync(selectionViewportPoint,version,token); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Selection text: "+ex.Message); }
            finally { selectionUpdateBusy = false; }
        }

        private static void AppendSelectedPageText(StringBuilder text,PageView view,int first,int last)
        {
            TextGlyph previous = null;
            for (var i=first;i<=last;i++)
            {
                var g=view.Glyphs[i];
                if (previous!=null && !string.IsNullOrWhiteSpace(g.Text) && !string.IsNullOrWhiteSpace(previous.Text))
                {
                    double lineOffset,gap,height; GlyphSpacing(view,previous,g,out lineOffset,out gap,out height);
                    if (lineOffset>height*.6) text.AppendLine();
                    else if (gap>height*.2) text.Append(' ');
                }
                text.Append(g.Text); previous=g;
            }
        }

        private async Task<string> SelectedTextAsync()
        {
            if (selectedTextPage == null) return string.Empty;
            var version=textSelectionVersion; var token=activeRenderToken;
            var firstPage=Math.Min(selectedTextPage.Index,SelectionEndpoint.Index);
            var lastPage=Math.Max(selectedTextPage.Index,SelectionEndpoint.Index);
            var text=new StringBuilder();
            for(var page=firstPage;page<=lastPage;page++)
            {
                var view=pageViews[(int)page];
                await EnsureSelectionTextAsync(view,version,token);
                if(version!=textSelectionVersion || token!=activeRenderToken) return string.Empty;
                int first,last;
                if(TryGetSelectionRange(view,out first,out last))
                { if(text.Length>0) text.AppendLine(); AppendSelectedPageText(text,view,first,last); }
                if(!view.IsRealized && !IsTextSelectionEndpoint(view)) view.Glyphs=null;
            }
            return text.ToString();
        }
    }
}
