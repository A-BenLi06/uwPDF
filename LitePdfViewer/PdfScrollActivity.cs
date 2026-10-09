using System;

namespace LitePdfViewer
{
    internal sealed class PdfScrollActivity
    {
        private double horizontal, vertical;
        private DateTime settledAt = DateTime.MinValue;
        private DateTime wheelQuietAt = DateTime.MinValue;
        public bool IsDirectManipulationActive { get; private set; }
        public int Direction { get; private set; }

        public PdfScrollActivity() { Reset(); }
        public void Reset()
        {
            horizontal = vertical = 0;
            settledAt = DateTime.MinValue;
            Direction = 1;
            // A file switch does not finish a still-active system gesture.
        }
        public void BeginGesture() { IsDirectManipulationActive = true; }
        public void EndGesture()
        {
            IsDirectManipulationActive = false;
            settledAt = DateTime.MinValue;
        }
        public void NoteWheel(DateTime now) { wheelQuietAt = now.AddMilliseconds(120); }
        public bool IsSettled(DateTime now) { return !IsDirectManipulationActive && now >= settledAt && now >= wheelQuietAt; }
        public void Observe(double horizontalOffset, double verticalOffset, bool intermediate, DateTime now)
        {
            var movedHorizontally = Math.Abs(horizontalOffset - horizontal) > 0.5;
            var movedVertically = Math.Abs(verticalOffset - vertical) > 0.5;
            if (movedHorizontally) horizontal = horizontalOffset;
            if (movedVertically)
            {
                Direction = verticalOffset > vertical ? 1 : -1;
                vertical = verticalOffset;
            }
            if (movedHorizontally || movedVertically)
                settledAt = intermediate ? now.AddMilliseconds(120) : DateTime.MinValue;
            else if (!intermediate) settledAt = DateTime.MinValue;
        }
    }
}
