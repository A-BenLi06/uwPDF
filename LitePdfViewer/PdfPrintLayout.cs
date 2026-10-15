using System;
using System.Collections.Generic;
using System.Globalization;

namespace LitePdfViewer
{
    internal struct PdfPrintPageLayout
    {
        public double X, Y, Width, Height;
        public uint PixelWidth, PixelHeight;
    }

    internal static class PdfPrintLayout
    {
        public static PdfPrintPageLayout Fit(double pdfWidth,double pdfHeight,double paperWidth,double paperHeight,
            double left,double top,double width,double height,double dpi)
        {
            if(!Positive(pdfWidth)||!Positive(pdfHeight)||!Positive(paperWidth)||!Positive(paperHeight)||!Positive(dpi) ||
                double.IsNaN(left)||double.IsNaN(top)||double.IsInfinity(left)||double.IsInfinity(top))
                throw new ArgumentException("Invalid print geometry.");
            var right=Math.Min(paperWidth,left+width); var bottom=Math.Min(paperHeight,top+height);
            left=Math.Max(0,left); top=Math.Max(0,top); width=right-left; height=bottom-top;
            if(!Positive(width)||!Positive(height)) throw new ArgumentException("Empty printable area.");
            var scale=Math.Min(width/pdfWidth,height/pdfHeight);
            var result=new PdfPrintPageLayout { Width=pdfWidth*scale, Height=pdfHeight*scale };
            result.X=left+(width-result.Width)/2; result.Y=top+(height-result.Height)/2;
            var pixelsX=Math.Ceiling(result.Width*dpi/96); var pixelsY=Math.Ceiling(result.Height*dpi/96);
            // One page at a time, including very large paper sizes. Preview uses
            // 96 dpi; output aims for 300 dpi within this single-page limit.
            var limit=Math.Min(1,Math.Sqrt(12000000/(pixelsX*pixelsY)));
            result.PixelWidth=(uint)Math.Max(1,Math.Floor(pixelsX*limit));
            result.PixelHeight=(uint)Math.Max(1,Math.Floor(pixelsY*limit));
            return result;
        }
        private static bool Positive(double value) { return value>0 && !double.IsInfinity(value) && !double.IsNaN(value); }

        public static bool TryPages(string text,int count,out int[] pages)
        {
            pages=null; if(count<=0) return false;
            if(string.IsNullOrWhiteSpace(text))
            { pages=new int[count]; for(var i=0;i<count;i++) pages[i]=i; return true; }
            var set=new SortedSet<int>();
            foreach(var part in text.Replace('，',',').Replace('–','-').Split(','))
            {
                var range=part.Trim().Split('-'); int first,last;
                if(range.Length>2 || !int.TryParse(range[0].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out first)) return false;
                last=first;
                if(range.Length==2 && !int.TryParse(range[1].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out last)) return false;
                if(first<1 || last<first || last>count) return false;
                for(var page=first-1;page<last;page++) set.Add(page);
            }
            if(set.Count==0) return false;
            pages=new int[set.Count]; set.CopyTo(pages); return true;
        }
    }
}
