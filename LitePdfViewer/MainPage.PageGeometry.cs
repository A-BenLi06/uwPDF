using System;
using System.Threading;
using System.Threading.Tasks;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private PdfViewportAnchor? pendingLayoutAnchor;
        private ulong layoutAnchorToken;
        private uint fitReferencePage;

        private void CaptureLayoutAnchor()
        {
            // ChangeView would interrupt an active fling or pinch. Geometry may
            // still be corrected, but never pull the user back into an old view.
            if (!IsScrollSettled() || !IsZoomSettled() || scrollActivity.IsDirectManipulationActive)
            {
                pendingLayoutAnchor = null;
                return;
            }
            if (pendingLayoutAnchor != null || pageViews.Count == 0) return;
            var top = DocumentScroller.VerticalOffset / Math.Max(0.01, DocumentScroller.ZoomFactor);
            var index = PageAtOffset(top);
            pendingLayoutAnchor = PdfViewportAnchor.Capture(index, top,
                pageTops[index], pageViews[index].LayoutHeight, PageBorderThickness);
            layoutAnchorToken = activeRenderToken;
        }

        private bool ConfirmPageAspect(PageView view, double aspect)
        {
            if (!(aspect > 0) || double.IsInfinity(aspect)) return false;
            view.AspectKnown = true;
            // Small per-page differences accumulate into a large offset in a
            // long document. The metadata is deterministic; keep its exact ratio.
            if (aspect == view.Aspect) return false;
            CaptureLayoutAnchor();
            view.Aspect = aspect;
            view.LayoutHeight = view.LayoutWidth * aspect;
            return true;
        }

        private void RefreshPageGeometry(PageView view)
        {
            UpdatePageSize(view);
            RebuildPageOffsets();
            WakeRenderer();
        }

        private async Task EnsurePageGeometryAsync(PageView view, ulong token,
            CancellationToken cancellation = default(CancellationToken))
        {
            if (token != activeRenderToken) throw new OperationCanceledException();
            if (view.AspectKnown) return;
            await renderGate.WaitAsync(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (token != activeRenderToken || document == null) throw new OperationCanceledException();
                if (view.AspectKnown) return;
                using (var page = document.GetPage(view.Index))
                {
                    var size = page.Size;
                    if (ConfirmPageAspect(view, size.Width > 0 ? size.Height / size.Width : double.NaN))
                        RefreshPageGeometry(view);
                    if (!view.AspectKnown) throw new InvalidOperationException("无法读取 PDF 页面尺寸。");
                }
            }
            finally { renderGate.Release(); }
        }

        private void RestoreLayoutAnchor()
        {
            if (pendingLayoutAnchor == null) return;
            if (layoutAnchorToken != activeRenderToken || !IsScrollSettled() ||
                !IsZoomSettled() || scrollActivity.IsDirectManipulationActive)
            {
                pendingLayoutAnchor = null;
                return;
            }
            // Wait for the changed Canvas to finish layout before supplying an
            // offset to ChangeView, which is constrained by ScrollableHeight.
            if (Math.Abs(PageStack.ActualHeight - PageStack.Height) > 0.5) return;
            var anchor = pendingLayoutAnchor.Value;
            pendingLayoutAnchor = null;
            if (anchor.Page >= pageViews.Count) return;
            var top = anchor.Restore(pageTops[anchor.Page], pageViews[anchor.Page].LayoutHeight, PageBorderThickness);
            var offset = Math.Min(DocumentScroller.ScrollableHeight, top * DocumentScroller.ZoomFactor);
            if (Math.Abs(offset - DocumentScroller.VerticalOffset) > 0.5)
                DocumentScroller.ChangeView(null, offset, null, true);
        }

        private PageView GetFitReferencePage()
        {
            return fitReferencePage < pageViews.Count ? pageViews[(int)fitReferencePage] : null;
        }

        private void RefitPreservingPosition()
        {
            var view = GetFitReferencePage();
            if (view == null || scrollActivity.IsDirectManipulationActive || !IsScrollSettled()) return;
            var zoom = FitZoom(view);
            if (Math.Abs(DocumentScroller.ZoomFactor - zoom) <= 0.005f) return;
            var top = DocumentScroller.VerticalOffset / Math.Max(0.01, DocumentScroller.ZoomFactor);
            if (pendingLayoutAnchor != null)
            {
                var anchor = pendingLayoutAnchor.Value;
                top = anchor.Restore(pageTops[anchor.Page], pageViews[anchor.Page].LayoutHeight, PageBorderThickness);
                pendingLayoutAnchor = null;
            }
            PageFrame.Width = Math.Max(lastColumnWidth + 2 * PageBorderThickness,
                Math.Max(1, DocumentScroller.ViewportWidth) / zoom);
            DocumentScroller.ChangeView(0, top * zoom, zoom, true);
        }
    }
}
