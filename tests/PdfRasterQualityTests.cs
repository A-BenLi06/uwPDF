using System;
using LitePdfViewer;
internal static class PdfRasterQualityTests
{
    public static int Main()
    {
        try
        {
            Check(PdfRasterQuality.NeedsRefinement(768,900),"A 768-pixel initial preview stayed blurry at a 900-pixel target");
            Check(PdfRasterQuality.NeedsRefinement(768,1000),"A moderate default-DPI upgrade was missed");
            Check(!PdfRasterQuality.NeedsRefinement(895,900),"Subpixel rounding queued repeated native renders");
            Check(!PdfRasterQuality.NeedsRefinement(1350,900),"Sharper cached image was needlessly downgraded");
            Check(!PdfRasterQuality.NeedsRefinement(95,100),"Tiny surfaces did not retain the pixel dead band");
            Check(PdfRasterQuality.VisibleScale==1.5,"Visible text supersampling changed unexpectedly");
            Console.WriteLine("PASS: initial preview upgrades, device-pixel refinement tolerance, stable rounding and sharper-cache retention.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
}
