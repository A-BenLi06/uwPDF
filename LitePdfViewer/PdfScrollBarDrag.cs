using System;

namespace LitePdfViewer
{
    // Pointer coordinates and native thumb dimensions are in the same DIP space.
    internal sealed class PdfScrollBarDrag
    {
        private readonly double minimum, maximum, travel, startY, startValue;
        public PdfScrollBarDrag(double y, double value, double min, double max,
            double trackHeight, double thumbTop, double thumbHeight)
        {
            minimum = min; maximum = Math.Max(min, max);
            travel = Math.Max(0, trackHeight - thumbHeight);
            startY = y;
            startValue = y >= thumbTop && y <= thumbTop + thumbHeight
                ? Clamp(value) : Clamp(minimum + (travel > 0 ? (y - thumbHeight / 2) / travel * (maximum - minimum) : 0));
        }
        public double ValueAt(double y)
        {
            return Clamp(startValue + (travel > 0 ? (y - startY) / travel * (maximum - minimum) : 0));
        }
        private double Clamp(double value) { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}
