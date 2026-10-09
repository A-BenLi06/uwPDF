using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private readonly PdfMemoryBudget memoryBudget = new PdfMemoryBudget();
        private long activeBitmapCacheBudget = PdfMemoryBudget.MaximumBytes;
        private bool memoryConstrained;
        private readonly object memoryEventLock = new object();
        private CoreDispatcher memoryDispatcher;
        private bool memoryMonitoring, memoryUpdateQueued;
        private int memoryMonitorGeneration;
        private ulong? announcedMemoryLimit;

        private void StartMemoryMonitoring()
        {
            lock (memoryEventLock)
            {
                if (memoryMonitoring) return;
                memoryDispatcher = Dispatcher;
                memoryMonitoring = true;
            }
            MemoryManager.AppMemoryUsageIncreased += MemoryUsageChanged;
            MemoryManager.AppMemoryUsageDecreased += MemoryUsageChanged;
            MemoryManager.AppMemoryUsageLimitChanging += MemoryLimitChanging;
            QueueMemoryUpdate();
        }

        private void StopMemoryMonitoring()
        {
            lock (memoryEventLock)
            {
                if (!memoryMonitoring) return;
                memoryMonitoring = false;
                memoryUpdateQueued = false;
                announcedMemoryLimit = null;
                memoryMonitorGeneration++;
            }
            MemoryManager.AppMemoryUsageIncreased -= MemoryUsageChanged;
            MemoryManager.AppMemoryUsageDecreased -= MemoryUsageChanged;
            MemoryManager.AppMemoryUsageLimitChanging -= MemoryLimitChanging;
        }

        private void MemoryUsageChanged(object sender, object args) { QueueMemoryUpdate(); }

        private void MemoryLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs args)
        {
            // This event precedes the property update. Keep NewLimit through
            // coalesced usage events until the property has caught up.
            lock (memoryEventLock)
            {
                if (!memoryMonitoring) return;
                announcedMemoryLimit = args.NewLimit;
            }
            QueueMemoryUpdate();
        }

        private void QueueMemoryUpdate()
        {
            int generation;
            CoreDispatcher dispatcher;
            lock (memoryEventLock)
            {
                if (!memoryMonitoring || memoryUpdateQueued) return;
                memoryUpdateQueued = true;
                generation = memoryMonitorGeneration;
                dispatcher = memoryDispatcher;
            }
            var ignored = DispatchMemoryUpdateAsync(dispatcher, generation);
        }

        private async Task DispatchMemoryUpdateAsync(CoreDispatcher dispatcher, int generation)
        {
            try
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ulong limit;
                    lock (memoryEventLock)
                    {
                        if (!memoryMonitoring || generation != memoryMonitorGeneration) return;
                        memoryUpdateQueued = false;
                        var currentLimit = MemoryManager.AppMemoryUsageLimit;
                        limit = announcedMemoryLimit ?? currentLimit;
                        if (announcedMemoryLimit == currentLimit) announcedMemoryLimit = null;
                    }
                    ApplyMemorySnapshot(MemoryManager.AppMemoryUsage, limit,
                        (PdfMemoryPressure)MemoryManager.AppMemoryUsageLevel);
                });
            }
            catch (Exception ex)
            {
                lock (memoryEventLock)
                    if (generation == memoryMonitorGeneration) memoryUpdateQueued = false;
                System.Diagnostics.Debug.WriteLine("Memory notification failed: " + ex.Message);
            }
        }

        private void ApplyMemorySnapshot(ulong usage, ulong limit, PdfMemoryPressure pressure)
        {
            var previousBudget = activeBitmapCacheBudget;
            var wasConstrained = memoryConstrained;
            memoryBudget.Update(usage, limit, pressure);
            activeBitmapCacheBudget = memoryBudget.Bytes;
            memoryConstrained = memoryBudget.Constrained;
            if (wasConstrained && !memoryConstrained)
            {
                // Give transient allocation failures one new attempt after safe
                // headroom returns, without retrying on every Low notification.
                ResetRenderFailures();
                foreach (var view in pageViews) view.TextFailed = false;
            }
            retiredRasters.SetByteLimit(memoryConstrained ? 0 : activeBitmapCacheBudget / 2);
            if (!retiredRasters.HasPending) ReleaseRetiredRenderSurfaces();

            if (memoryConstrained)
            {
                int first, last;
                GetVisiblePageRange(out first, out last);
                var keepForeground = Window.Current.Visible;
                foreach (var index in new List<int>(cachedPages))
                {
                    var view = pageViews[index];
                    if (!keepForeground || index < first || index > last) ReleasePageBitmap(view);
                    else ReleasePageDetail(view);
                }
                for (var i = realizedPages.Count - 1; i >= 0; i--)
                {
                    var index = realizedPages[i];
                    if (!keepForeground || index < first || index > last) VirtualizePage(pageViews[index]);
                }
                foreach (var item in thumbnailItems.Values) ReleaseThumbnail(item);
            }
            else if (activeBitmapCacheBudget < previousBudget) TrimBitmapCache(null, 0);

            // A foreground base image stays attached until its smaller replacement
            // is ready. Dirty strokes/highlights are retained by virtualization.
            if (activeBitmapCacheBudget != previousBudget || memoryConstrained != wasConstrained)
            {
                System.Diagnostics.Debug.WriteLine("PDF raster budget: " + activeBitmapCacheBudget +
                    " bytes, constrained=" + memoryConstrained);
                WakeRenderer();
            }
        }

        private bool IsPageVisible(uint index)
        {
            int first, last;
            GetVisiblePageRange(out first, out last);
            return index >= first && index <= last;
        }
    }
}
