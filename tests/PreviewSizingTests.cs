using System;
using LitePdfViewer;

internal static class PreviewSizingTests
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            foreach(var display in new[]{new[]{1920d,1200,1.5},new[]{2736d,1824,2},new[]{1920d,1080,1},new[]{1280d,800,2}})
            foreach(var thumbnails in new[]{false,true})
            {
                var workWidth=display[0]/display[2]; var workHeight=display[1]/display[2]-40;
                var maxWidth=workWidth-64; var maxHeight=workHeight-64;
                foreach(var page in new[]{new[]{612d,792},new[]{595d,842},new[]{1000d,1400},new[]{1400d,1000},new[]{200d,3000}})
                {
                    var size=PreviewSizing.Calculate(page[0],page[1],thumbnails,workWidth,workHeight);
                    var fixedWidth=28+(thumbnails?110:0);
                    Check(size.Scale==1,"Window sizing implicitly shrank the document");
                    Check(Math.Abs(size.Width-Math.Min(maxWidth,page[0]+fixedWidth))<.001,"Page height reduced the readable window width");
                    Check(size.Width<=maxWidth && size.Height<=maxHeight,"Automatic window exceeds work area");
                    Check(Math.Abs(size.Height-Math.Min(maxHeight,page[1]+60))<.001,"Window height did not clamp independently");
                }
            }
            var fallback=PreviewSizing.Calculate(0,0,true,1280,800);
            Check(fallback.Width==800 && fallback.Height==600 && fallback.Scale==1,"Unknown page-size fallback changed");
            Console.WriteLine("PASS: ThinkPad 1920x1200 at 150%, Surface 2736x1824 at 200%, and small/large displays keep readable page width while independently clamping window height; document scale stays 100%.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
