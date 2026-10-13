using System;
using LitePdfViewer;

internal static class PdfScrollBarDragTests
{
    private static void Equal(double actual, double expected)
    { if (Math.Abs(actual - expected) > .001) throw new Exception(actual + " != " + expected); }
    public static int Main()
    {
        var drag = new PdfScrollBarDrag(110, 250, 0, 1000, 500, 100, 100);
        Equal(drag.ValueAt(110), 250); // Grabbing off-center must not jump.
        Equal(drag.ValueAt(210), 500);
        Equal(drag.ValueAt(-1000), 0);
        Equal(drag.ValueAt(1000), 1000);
        drag = new PdfScrollBarDrag(400, 250, 0, 1000, 500, 100, 100);
        Equal(drag.ValueAt(400), 875); // Track contact centers the thumb.
        Equal(drag.ValueAt(350), 750);
        drag = new PdfScrollBarDrag(20, 6, 5, 10, 44, 0, 44);
        Equal(drag.ValueAt(9999), 6); // No usable travel, no division by zero.
        drag = new PdfScrollBarDrag(20, 0, 0, 1000000, 500, 0, 44);
        Equal(drag.ValueAt(476), 1000000); // Minimum-size thumb on a long document.
        Console.WriteLine("PASS: touch thumb grab, proportional drag, track contact, bounds, minimum thumb and zero-travel geometry.");
        return 0;
    }
}
