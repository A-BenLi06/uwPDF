using System;
using System.Linq;
using LitePdfViewer;

internal static class PdfPrintLayoutTests
{
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            foreach(var pdf in new[]{new[]{500d,700},new[]{700d,500},new[]{100d,4000},new[]{4000d,100}})
            foreach(var paper in new[]{new[]{816d,1056},new[]{1056d,816},new[]{3000d,5000}})
            foreach(var dpi in new[]{96d,300})
            {
                var p=PdfPrintLayout.Fit(pdf[0],pdf[1],paper[0],paper[1],24,36,paper[0]-60,paper[1]-84,dpi);
                Check(p.X>=24-.001 && p.Y>=36-.001 && p.X+p.Width<=paper[0]-36+.001 && p.Y+p.Height<=paper[1]-48+.001,"Print image exceeds hardware margins");
                Check(Math.Abs(p.Width/p.Height-pdf[0]/pdf[1])<.0001,"Print image distorted");
                Check(p.PixelWidth>0 && p.PixelHeight>0 && (long)p.PixelWidth*p.PixelHeight<=12000000,"Unbounded single-page allocation");
                Check(Math.Abs((p.X-24)-(paper[0]-36-p.X-p.Width))<.001,"Unequal horizontal centering");
            }
            int[] pages;
            Check(PdfPrintLayout.TryPages(null,600,out pages) && pages.Length==600 && pages[599]==599,"Default all-page range");
            Check(PdfPrintLayout.TryPages("5, 1-3,2,7–8，10",10,out pages) && pages.SequenceEqual(new[]{0,1,2,4,6,7,9}),"Ordered unique inclusive ranges and localized punctuation");
            foreach(var invalid in new[]{"0","-1","3-2","1-11","11","1,,2","1-2-3","abc","+1","2147483648","1,"})
                Check(!PdfPrintLayout.TryPages(invalid,10,out pages),"Invalid range accepted: "+invalid);
            var failed=false;
            try { PdfPrintLayout.Fit(0,700,816,1056,0,0,816,1056,300); } catch(ArgumentException) { failed=true; }
            Check(failed,"Invalid print page geometry accepted");
            Console.WriteLine("PASS: print layout preserves aspect, honors asymmetric hardware margins, centers portrait/landscape pages, bounds raster memory, and validates ordered page ranges.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
