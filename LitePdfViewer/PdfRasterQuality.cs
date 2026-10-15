using System;

namespace LitePdfViewer
{
    internal static class PdfRasterQuality
    {
        // A modest supersample sharpens small PDF text at fit/default zoom.
        // Allocation remains subject to per-page and shared raster budgets.
        public const double VisibleScale = 1.5;
        public static double BaseWidthLimit(double zoom, bool visible)
        {
            return !visible ? 768 : zoom > 1.25 ? 1024 : 2560;
        }
        public static bool NeedsRefinement(double renderedWidth, double targetWidth)
        {
            return targetWidth - renderedWidth > Math.Max(8, targetWidth * 0.05);
        }
    }
}
