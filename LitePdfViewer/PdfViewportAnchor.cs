using System;

namespace LitePdfViewer
{
    internal struct PdfViewportAnchor
    {
        public int Page;
        private double position;
        private int region;

        public static PdfViewportAnchor Capture(int page, double offset,
            double pageTop, double pageHeight, double border)
        {
            var local = offset - pageTop - border;
            return new PdfViewportAnchor {
                Page = page,
                region = local < 0 ? -1 : local > pageHeight ? 1 : 0,
                position = local < 0 ? local : local > pageHeight ? local - pageHeight : local / Math.Max(double.Epsilon, pageHeight)
            };
        }

        public double Restore(double pageTop, double pageHeight, double border)
        {
            var local = region < 0 ? position : region > 0 ? pageHeight + position : position * pageHeight;
            return Math.Max(0, pageTop + border + local);
        }
    }
}
