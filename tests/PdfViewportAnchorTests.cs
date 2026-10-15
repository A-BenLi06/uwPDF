using System;
using LitePdfViewer;

class PdfViewportAnchorTests
{
    static void Expect(double actual, double expected)
    {
        if (Math.Abs(actual - expected) > .000001)
            throw new Exception("Expected offset " + expected + ", got " + actual);
    }

    static int Main()
    {
        var middle = PdfViewportAnchor.Capture(500, 400321, 400000, 800, 1);
        if (middle.Page != 500) throw new Exception("Anchor page changed.");
        // Corrections above the viewport move its top; a corrected current
        // page keeps the same PDF fraction, rather than jumping to page start.
        Expect(middle.Restore(350000, 600, 1), 350241);
        Expect(middle.Restore(410000, 800, 1), 410321);
        Expect(middle.Restore(410000, 400, 1), 410161);
        // Multiple batches use the original snapshot, without accumulating drift.
        Expect(middle.Restore(350020, 600, 1), 350261);
        // A tenth of a DIP per preceding page becomes a 60-DIP correction in
        // a 600-page file; it must preserve the reading point too.
        var oldTop = 600 * (800 + 12);
        var subtle = PdfViewportAnchor.Capture(600, oldTop + 401, oldTop, 800, 1);
        Expect(subtle.Restore(600 * (800.1 + 12), 800.1, 1), oldTop + 461.05);
        // Margins/borders preserve DIP distance instead of stretching with a page.
        var before = PdfViewportAnchor.Capture(2, 1998, 2000, 800, 1);
        Expect(before.Restore(1800, 600, 1), 1798);
        var after = PdfViewportAnchor.Capture(2, 2801.5, 2000, 800, 1);
        Expect(after.Restore(1800, 600, 1), 2401.5);
        // Zoom lives outside the unzoomed anchor geometry.
        var zoomed = PdfViewportAnchor.Capture(2, 4402 / 2.0, 2000, 800, 1);
        Expect(zoomed.Restore(1800, 600, 1) * 2, 3902);
        var tiny = PdfViewportAnchor.Capture(0, 1.05, 0, .1, 1);
        Expect(tiny.Restore(0, .2, 1), 1.1);
        var leading = PdfViewportAnchor.Capture(0, 0, 0, 800, 1);
        Expect(leading.Restore(0, 600, 1), 0);
        Console.WriteLine("PASS: mixed-page correction preserves page fraction, borders, gaps, zoom and tiny-page geometry");
        return 0;
    }
}
