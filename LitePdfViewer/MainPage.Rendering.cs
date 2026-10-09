using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        internal void RecoverRenderSurfaces()
        {
            rasterBackend.ResetDevice();
            ClearLostRenderSurfaces();
        }

        private void ClearLostRenderSurfaces()
        {
            ReleaseRetiredRenderSurfaces();
            foreach (var index in new System.Collections.Generic.List<int>(cachedPages))
            {
                var view = pageViews[index];
                if (view.Source is SurfaceImageSource) ReleasePageBitmap(view);
                else if (view.DetailRaster != null && view.DetailRaster.Source is SurfaceImageSource)
                    ReleasePageDetail(view);
            }
            foreach (var item in thumbnailItems.Values)
            {
                ReleaseThumbnail(item);
                item.IsRendered = false;
            }
            ResetRenderFailures();
            WakeRenderer();
        }

        private void ResetRenderFailures()
        {
            // First-draw failures never entered cachedPages. They must also get
            // another attempt after device/memory recovery, rather than stay blank.
            foreach (var view in pageViews)
            {
                view.RenderFailed = false;
                view.DetailFailed = false;
            }
            foreach (var item in thumbnailItems.Values) item.RenderFailed = false;
        }
        private void AttachDetailLayer(PageView view)
        {
            view.DetailLayer = new Canvas { IsHitTestVisible = false };
            view.DetailImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            view.DetailLayer.Children.Add(view.DetailImage);
            // Keep PDF detail below highlights, ink, and the selection overlay.
            view.Host.Children.Insert(1, view.DetailLayer);
            UpdateDetailLayer(view);
        }

        private static void UpdateDetailLayer(PageView view)
        {
            if (view.DetailLayer == null) return;
            view.DetailLayer.Width = view.LayoutWidth;
            view.DetailLayer.Height = view.LayoutHeight;
            var region = view.DetailRegion;
            view.DetailImage.Width = region.Width * view.LayoutWidth;
            view.DetailImage.Height = region.Height * view.LayoutHeight;
            Canvas.SetLeft(view.DetailImage, region.X * view.LayoutWidth);
            Canvas.SetTop(view.DetailImage, region.Y * view.LayoutHeight);
            view.DetailImage.Source = view.DetailRaster == null ? null : view.DetailRaster.Source;
        }

        private Rect VisiblePageRegion(PageView view, double padding)
        {
            var zoom = Math.Max(0.01, DocumentScroller.ZoomFactor);
            var left = DocumentScroller.HorizontalOffset / zoom;
            var top = DocumentScroller.VerticalOffset / zoom - pageTops[(int)view.Index] - PageBorderThickness;
            var width = DocumentScroller.ViewportWidth / zoom;
            var height = DocumentScroller.ViewportHeight / zoom;
            // Include a small runway so slow panning doesn't redraw each pixel.
            left = Math.Max(0, left - padding);
            var right = Math.Min(view.LayoutWidth, DocumentScroller.HorizontalOffset / zoom + width + padding);
            var bottom = Math.Min(view.LayoutHeight, top + height + padding);
            top = Math.Max(0, top - padding);
            if (right <= left || bottom <= top) return new Rect();
            return new Rect(left / view.LayoutWidth, top / view.LayoutHeight,
                (right - left) / view.LayoutWidth, (bottom - top) / view.LayoutHeight);
        }

        private PageView FindNextDetailPage()
        {
            if (memoryConstrained || !IsScrollSettled() || !IsZoomSettled()) return null;
            int first, last;
            GetVisiblePageRange(out first, out last);
            for (var i = first; i <= last && i < pageViews.Count; i++)
            {
                var view = pageViews[i];
                var scale = view.LayoutWidth * dpiScale * DocumentScroller.ZoomFactor;
                if (DocumentScroller.ZoomFactor < 1.25 || view.RenderedWidth >= scale * 0.85)
                {
                    ReleasePageDetail(view);
                    continue;
                }
                if (!view.IsRendered || !view.IsRealized || view.DetailFailed) continue;
                var region = VisiblePageRegion(view, 0);
                if (region.Width <= 0 || region.Height <= 0) continue;
                var old = view.DetailRegion;
                if (view.DetailRaster == null || scale > view.DetailScale * 1.2 ||
                    region.X < old.X - 0.001 || region.Y < old.Y - 0.001 ||
                    region.Right > old.Right + 0.001 || region.Bottom > old.Bottom + 0.001) return view;
            }
            return null;
        }

        private async Task RenderPageDetailAsync(PageView view, ulong token)
        {
            PdfPageRaster pending = null;
            await renderGate.WaitAsync();
            try
            {
                if (memoryConstrained || !PageStillWanted(view.Index, token) || !view.IsRealized ||
                    !IsScrollSettled() || !IsZoomSettled()) return;
                var region = VisiblePageRegion(view, 64 / DocumentScroller.ZoomFactor);
                if (region.Width <= 0 || region.Height <= 0) return;
                var scale = view.LayoutWidth * dpiScale * DocumentScroller.ZoomFactor;
                var width = scale * region.Width;
                var height = view.LayoutHeight * dpiScale * DocumentScroller.ZoomFactor * region.Height;
                int first, last;
                GetVisiblePageRange(out first, out last);
                // Reserve a bounded portion for detail; account for the fallback's
                // CPU and GPU copies as well. No full-page 400%-scale allocation.
                var maxPixels = Math.Min(2000000, activeBitmapCacheBudget / 8.0 * 0.25 / Math.Max(1, last - first + 1));
                var shrink = Math.Min(1, Math.Sqrt(maxPixels / (width * height)));
                using (var page = document.GetPage(view.Index))
                {
                    var size = page.Size;
                    var source = new Rect(region.X * size.Width, region.Y * size.Height,
                        region.Width * size.Width, region.Height * size.Height);
                    pending = await rasterBackend.RenderAsync(page, (uint)Math.Max(1, width * shrink),
                        (uint)Math.Max(1, height * shrink), source);
                }
                if (memoryConstrained || !PageStillWanted(view.Index, token) || !view.IsRealized ||
                    !IsScrollSettled() || !IsZoomSettled()) return;
                var bytes = view.BitmapBytes - (view.DetailRaster == null ? 0 : view.DetailRaster.Bytes) + pending.Bytes;
                var previousRaster = view.DetailRaster;
                // Count the old patch until its presentation grace period ends.
                TrimBitmapCache(view, bytes + (previousRaster == null ? 0 : previousRaster.Bytes));
                var previousBytes = previousRaster == null ? 0 : previousRaster.Bytes;
                view.BitmapBytes -= previousBytes;
                cachedBitmapBytes -= previousBytes;
                view.DetailRaster = pending;
                view.DetailRegion = region;
                // Store the requested scale to avoid repeatedly refining a patch
                // whose resolution was deliberately capped by the pixel budget.
                view.DetailScale = scale;
                view.BitmapBytes += pending.Bytes;
                cachedBitmapBytes += pending.Bytes;
                pending = null;
                UpdateDetailLayer(view);
                RetireRaster(previousRaster, true);
            }
            finally
            {
                if (pending != null) pending.Dispose();
                renderGate.Release();
            }
        }

        private void ReleasePageDetail(PageView view)
        {
            if (view.DetailImage != null) view.DetailImage.Source = null;
            if (view.DetailRaster == null) return;
            cachedBitmapBytes -= view.DetailRaster.Bytes;
            view.BitmapBytes -= view.DetailRaster.Bytes;
            view.DetailRaster.Dispose();
            view.DetailRaster = null;
            view.DetailRegion = new Rect();
            view.DetailScale = 0;
        }

        private void WakeRenderer()
        {
            if (renderWakeQueued) return;
            renderWakeQueued = true;
            // Run after the input/layout callback has finished. Completing a TCS
            // inline can otherwise re-enter the scheduler halfway through a jump.
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                renderWakeQueued = false;
                var previous = renderWake;
                renderWake = new TaskCompletionSource<bool>();
                previous.TrySetResult(true);
            });
        }

        private void RetireRaster(PdfPageRaster raster, bool wasAttached)
        {
            if (raster == null) return;
            if (!wasAttached || !Windows.UI.Xaml.Window.Current.Visible) { raster.Dispose(); return; }
            retiredRasters.Retire(raster, raster.Bytes);
            if (!retirementRenderingSubscribed && retiredRasters.HasPending)
            {
                retirementRenderingSubscribed = true;
                CompositionTarget.Rendering += RetiredRaster_Rendering;
            }
        }

        private void RetiredRaster_Rendering(object sender, object args)
        {
            var frame = args as RenderingEventArgs;
            if (frame != null) retiredRasters.AdvanceFrame(frame.RenderingTime);
            if (!retiredRasters.HasPending) ReleaseRetiredRenderSurfaces();
        }

        internal void ReleaseRetiredRenderSurfaces()
        {
            if (retirementRenderingSubscribed)
            {
                CompositionTarget.Rendering -= RetiredRaster_Rendering;
                retirementRenderingSubscribed = false;
            }
            retiredRasters.Clear();
        }

        private async Task WaitForRenderWorkAsync(int milliseconds = 0)
        {
            var wake = renderWake.Task;
            if (milliseconds > 0) await Task.WhenAny(wake, Task.Delay(milliseconds));
            else await wake;
        }

        private bool IsScrollSettled() { return scrollActivity.IsSettled(DateTime.UtcNow); }

        private void RestoreVisiblePages()
        {
            int first, last;
            GetVisiblePageRange(out first, out last);
            for (var i = first; i <= last && i < pageViews.Count; i++)
            {
                var view = pageViews[i];
                if (view.Source == null || view.IsRealized) continue;
                RealizePage(view);
                if (!view.InkLoaded && !view.IsLoadingInk)
                {
                    var ignored = RestoreVisibleInkAsync(view, activeRenderToken);
                }
            }
        }

        private async Task RestoreVisibleInkAsync(PageView view, ulong token)
        {
            try { await LoadInkAsync(view); }
            catch (Exception ex)
            {
                if (token == activeRenderToken)
                    System.Diagnostics.Debug.WriteLine("Annotation load failed: " + ex.Message);
            }
        }

        private int PageAtOffset(double offset)
        {
            int lo = 0, hi = pageViews.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (pageTops[mid] + pageViews[mid].LayoutHeight + 2 * PageBorderThickness < offset) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private void GetVisiblePageRange(out int first, out int last)
        {
            first = last = 0;
            if (pageViews.Count == 0 || pageTops.Length != pageViews.Count) return;
            var zoom = Math.Max(0.01, DocumentScroller.ZoomFactor);
            var top = DocumentScroller.VerticalOffset / zoom;
            var height = Math.Max(1, DocumentScroller.ViewportHeight) / zoom;
            first = PageAtOffset(top);
            last = PageAtOffset(top + height);
        }

        private bool PageStillWanted(uint index, ulong token)
        {
            if (token != activeRenderToken || document == null || index >= pageViews.Count) return false;
            int first, last;
            GetVisiblePageRange(out first, out last);
            return index >= Math.Max(0, first - RenderWindow) && index <= last + RenderWindow;
        }

        private PageView FindNextTextPage()
        {
            if (textWorkerBusy) return null;
            int first, last;
            GetVisiblePageRange(out first, out last);
            for (var i = first; i <= last && i < pageViews.Count; i++)
            {
                var view = pageViews[i];
                if (view.IsRealized && view.IsRendered && view.Glyphs == null && !view.TextFailed) return view;
            }
            return null;
        }

        private async Task RunTextWorkAsync(PageView view, ulong token)
        {
            textWorkerBusy = true;
            try { await EnsurePageTextAsync(view, token); }
            finally
            {
                if (token == activeRenderToken) { textWorkerBusy = false; WakeRenderer(); }
            }
        }

        private void TrimBitmapCache(PageView incoming, long bytes)
        {
            int first, last;
            GetVisiblePageRange(out first, out last);
            var replacedBytes = incoming == null ? 0 : incoming.BitmapBytes;
            while (cachedBitmapBytes + retiredRasters.Bytes - replacedBytes + bytes > activeBitmapCacheBudget)
            {
                PageView victim = null;
                var farthest = -1;
                foreach (var index in cachedPages)
                {
                    var view = pageViews[index];
                    if (view == incoming || view.Source == null || (index >= first && index <= last)) continue;
                    var distance = Math.Abs(index - (int)pageIndex);
                    if (distance > farthest) { farthest = distance; victim = view; }
                }
                if (victim == null) break;
                ReleasePageBitmap(victim);
            }
        }
    }
}
