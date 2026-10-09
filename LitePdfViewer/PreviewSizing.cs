using System;

namespace LitePdfViewer
{
    // PDF point dimensions are used as logical DIP dimensions for the Quick Look
    // visual scale, rather than converting them to Windows' 96-DPI physical size.
    internal static class PreviewSizing
    {
        public const double HorizontalPadding = 28;
        public const double VerticalPadding = 16;
        public const double TopChrome = 52;
        public const double ThumbnailRail = 110;
        public const double ScreenMargin = 32;

        public static PreviewSize Calculate(double pageWidth, double pageHeight, bool thumbnails, double workWidth, double workHeight)
        {
            var maxWidth = Math.Max(1, workWidth - ScreenMargin * 2);
            var maxHeight = Math.Max(1, workHeight - ScreenMargin * 2);
            if (!Valid(pageWidth) || !Valid(pageHeight))
                return new PreviewSize { Width = Math.Min(800, maxWidth), Height = Math.Min(600, maxHeight), Scale = 1 };

            var fixedWidth = HorizontalPadding + (thumbnails ? ThumbnailRail : 0);
            var fixedHeight = TopChrome + VerticalPadding;
            var scale = Math.Max(0, Math.Min(1, Math.Min((maxWidth - fixedWidth) / pageWidth, (maxHeight - fixedHeight) / pageHeight)));
            return new PreviewSize { Width = Math.Min(maxWidth, pageWidth * scale + fixedWidth),
                Height = Math.Min(maxHeight, pageHeight * scale + fixedHeight), Scale = scale };
        }

        private static bool Valid(double value) { return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    internal struct PreviewSize
    {
        public double Width, Height, Scale;
    }
}
