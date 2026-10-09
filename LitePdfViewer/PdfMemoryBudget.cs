using System;

namespace LitePdfViewer
{
    internal enum PdfMemoryPressure { Low, Medium, High, OverLimit }

    // Event-driven policy; no memory query on each input/composition frame.
    internal sealed class PdfMemoryBudget
    {
        private const long MiB = 1024 * 1024;
        public const long MaximumBytes = 64 * MiB;
        public long Bytes { get; private set; } = MaximumBytes;
        public bool Constrained { get; private set; }

        public void Update(ulong usage, ulong limit, PdfMemoryPressure pressure)
        {
            // Subtraction/division avoid overflow for the UInt64 API values.
            var over = pressure == PdfMemoryPressure.OverLimit || (limit != 0 && usage >= limit);
            var high = over || pressure == PdfMemoryPressure.High ||
                (limit != 0 && usage >= limit - limit / 10);
            var safelyLow = pressure == PdfMemoryPressure.Low &&
                (limit == 0 || usage < limit - limit / 10 * 3);
            // Don't immediately refill after an eviction merely crosses High ->
            // Medium. Wait for a Low notification with sufficient headroom.
            Constrained = high || (Constrained && !safelyLow);
            var bytes = limit == 0 ? MaximumBytes : (long)Math.Min((ulong)MaximumBytes, limit / 8);
            if (Constrained) bytes = Math.Min(bytes, (over ? 4 : 8) * MiB);
            else if (pressure == PdfMemoryPressure.Medium) bytes = Math.Min(bytes, 32 * MiB);
            // Keep a small readable foreground budget even if the OS has already
            // lowered its limit below current usage. This isn't a total-app cap.
            Bytes = Math.Max(MiB, bytes);
        }
    }
}
