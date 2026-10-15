using System;
namespace LitePdfViewer
{
    internal static class PdfSelectionScroll
    {
        public static int Direction(double y,double height)
        { return height<=0 ? 0 : y<40 ? -1 : y>height-40 ? 1 : 0; }
        public static double Speed(double y,double height,double dwellSeconds)
        {
            var direction=Direction(y,height);
            if(direction==0) return 0;
            var depth=direction<0 ? (40-y)/40 : (y-height+40)/40;
            var acceleration=Math.Max(0,Math.Min(2,dwellSeconds-.5));
            return direction*Math.Min(2200,80+520*depth*depth+600*acceleration);
        }
    }
}
