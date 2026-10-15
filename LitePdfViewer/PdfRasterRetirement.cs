using System;
using System.Collections.Generic;

namespace LitePdfViewer
{
    // UI-thread queue. Preserve replaced rasters through a subsequent XAML frame.
    // This presentation grace period is not a GPU completion fence.
    internal sealed class PdfRasterRetirement
    {
        private sealed class Entry
        {
            public IDisposable Resource;
            public long Bytes;
            public long ReleaseFrame;
        }
        private readonly Queue<Entry> entries = new Queue<Entry>();
        private long byteLimit;
        private readonly int countLimit;
        private long frame;
        private TimeSpan lastFrame = TimeSpan.MinValue;
        public long Bytes { get; private set; }
        public bool HasPending { get { return entries.Count != 0; } }

        public PdfRasterRetirement(long byteLimit, int countLimit)
        {
            if (byteLimit <= 0 || countLimit <= 0) throw new ArgumentOutOfRangeException();
            this.byteLimit = byteLimit;
            this.countLimit = countLimit;
        }
        public void Retire(IDisposable resource, long bytes)
        {
            if (resource == null) return;
            if (bytes < 0) throw new ArgumentOutOfRangeException("bytes");
            // Memory pressure takes precedence over the presentation grace period.
            if (byteLimit == 0 || bytes > byteLimit) { resource.Dispose(); return; }
            while (entries.Count >= countLimit || Bytes > byteLimit - bytes) ReleaseFirst();
            entries.Enqueue(new Entry { Resource = resource, Bytes = bytes, ReleaseFrame = frame + 2 });
            Bytes += bytes;
        }
        public void SetByteLimit(long bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException("bytes");
            byteLimit = bytes;
            while (HasPending && (Bytes > byteLimit || byteLimit == 0)) ReleaseFirst();
        }
        public void AdvanceFrame(TimeSpan renderingTime)
        {
            // Duplicate callbacks for one frame must not shorten retention.
            if (renderingTime <= lastFrame) return;
            lastFrame = renderingTime;
            frame++;
            while (HasPending && entries.Peek().ReleaseFrame <= frame) ReleaseFirst();
        }
        public void Clear() { while (HasPending) ReleaseFirst(); }
        private void ReleaseFirst()
        {
            var entry = entries.Dequeue();
            Bytes -= entry.Bytes;
            entry.Resource.Dispose();
        }
    }
}
