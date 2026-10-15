using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        public async void RunZoomQualityQa()
        {
            var folder=await ApplicationData.Current.LocalFolder.GetFolderAsync("zoom-quality-qa");
            var report=new System.Text.StringBuilder();
            try
            {
                await LoadDocumentAsync(await folder.GetFileAsync("source.pdf"));
                await Task.Delay(1200);
                var view=pageViews[0];
                var expected=ComputeRenderTargetWidth(view);
                for(var i=0;i<100 && (view.NeedsRender(expected) || !view.IsRendered);i++) await Task.Delay(40);
                QaZoomAssert(view.IsRendered && !view.NeedsRender(expected),"Default preview was not refined to settled resolution");
                report.AppendLine("Default: zoom="+DocumentScroller.ZoomFactor+", DPI="+dpiScale+", raster="+view.RenderedWidth+", target="+expected+", pixels="+view.RenderedWidth*view.RenderedWidth*view.Aspect);
                QaZoomAssert(view.RenderedWidth > Math.Min(768,view.LayoutWidth*dpiScale*DocumentScroller.ZoomFactor)+20,"Default resolution did not improve over the old initial preview");
                QaZoomAssert(view.RenderedWidth*view.RenderedWidth*view.Aspect <= RenderPixelBudget(view)+4000,"Default raster exceeded pixel budget");
                await QaZoomBitmapAsync(MainChrome,folder,"default-sharp.png");
                PdfPageRaster comparison=null;
                try
                {
                    var oldWidth=Math.Min(768,view.LayoutWidth*dpiScale*DocumentScroller.ZoomFactor);
                    using(var page=document.GetPage(0)) comparison=await rasterBackend.RenderAsync(page,(uint)oldWidth,(uint)(oldWidth*view.Aspect),new Rect());
                    view.Image.Source=comparison.Source;
                    await QaZoomBitmapAsync(MainChrome,folder,"default-preview-before.png");
                }
                finally { view.Image.Source=view.Source; if(comparison!=null) comparison.Dispose(); }
                report.AppendLine("PASS: visible default raster supersampled, low-resolution first preview refined, allocation remains bounded; native control renderings captured.");

                // A 768-pixel startup image used to be kept at a 900-pixel
                // settled target because the 30% refinement threshold missed it.
                SetZoom((float)(900 / (view.LayoutWidth*dpiScale*PdfRasterQuality.VisibleScale)),false);
                await Task.Delay(450); expected=ComputeRenderTargetWidth(view);
                for(var i=0;i<100 && view.NeedsRender(expected);i++) await Task.Delay(40);
                QaZoomAssert(view.RenderedWidth>=expected-1 && view.RenderedWidth>850,"Moderately sharper target retained its startup preview");
                report.AppendLine("PASS: native settled 900-pixel target upgrades the former 768-pixel preview.");

                // No XAML PointerPressed: native view changes emulate the event
                // sequence used by a precision touchpad, without input injection.
                fitPageToViewport=true; programmaticZoomTarget=null;
                DocumentScroller.ChangeView(null,null,1.25f,false);
                await Task.Delay(1100); ScheduleRelayout(); await Task.Delay(700);
                QaZoomAssert(!fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-1.25f)<.005,"Pointerless native zoom snapped back to fit");
                report.AppendLine("PASS: pointerless native zoom leaves fit and remains at 125% through final view events and delayed layout.");

                FitToWindow(true); await Task.Delay(900);
                QaZoomAssert(fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-FitZoom(GetFitReferencePage(),false))<.005,"Programmatic fit was mistaken for native user input");
                SetZoom(1,false); await Task.Delay(400);
                ZoomSlider.Value=80; ZoomInButton_Click(null,null);
                await Task.Delay(1000);
                QaZoomAssert(Math.Abs(DocumentScroller.ZoomFactor-1.25f)<.005 && pendingSliderZoom==null,"Stale slider work reset a newer zoom button request");
                SetZoom(1,false); await Task.Delay(400);
                ZoomSlider.Value=80; FitToWindow(true); await Task.Delay(1000);
                QaZoomAssert(fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-FitZoom(GetFitReferencePage(),false))<.005,"Stale slider work reset an explicit fit");
                report.AppendLine("PASS: native programmatic fit preserved; pending slider values cannot override zoom buttons or fit.");

                foreach(var target in new[]{.64f,.8f,1f,1.25f,1.6f,2f})
                {
                    SetZoom(target,true); await Task.Delay(1000);
                    QaZoomAssert(!fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-target)<.005,"Manual zoom reverted at "+target);
                }
                SetZoom(2,true); await Task.Delay(35); SetZoom(.8f,true); await Task.Delay(35); FitToWindow(true); await Task.Delay(1100);
                QaZoomAssert(fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-FitZoom(GetFitReferencePage(),false))<.005,"Rapid retarget followed by fit lost its final request");
                report.AppendLine("PASS: native animated 64/80/100/125/160/200% zoom and rapid retargeting settle at the requested view.");
                var originalDpi=dpiScale; var originalZoom=DocumentScroller.ZoomFactor;
                RefreshDisplayScale(originalDpi*1.5); await Task.Delay(900);
                expected=ComputeRenderTargetWidth(view);
                for(var i=0;i<100 && view.NeedsRender(expected);i++) await Task.Delay(40);
                QaZoomAssert(!view.NeedsRender(expected) && Math.Abs(DocumentScroller.ZoomFactor-originalZoom)<.005,"DPI change failed to refine without changing zoom");
                RefreshDisplayScale(originalDpi);
                report.AppendLine("PASS: display scale update rerenders to the new device-pixel target while preserving zoom.");
                report.AppendLine("PASS: zoom and raster quality QA completed. Physical precision-touchpad operation still requires device acceptance.");
            }
            catch(Exception ex) { report.AppendLine("FAIL: "+ex); }
            await FileIO.WriteTextAsync(await folder.CreateFileAsync("report.txt",CreationCollisionOption.ReplaceExisting),report.ToString());
        }
        private static void QaZoomAssert(bool value,string label) { if(!value) throw new Exception(label); }
        private static async Task QaZoomBitmapAsync(FrameworkElement element,StorageFolder folder,string name)
        {
            var target=new RenderTargetBitmap();await target.RenderAsync(element);
            var pixels=await target.GetPixelsAsync(); var data=new byte[pixels.Length];DataReader.FromBuffer(pixels).ReadBytes(data);
            using(var stream=await (await folder.CreateFileAsync(name,CreationCollisionOption.ReplaceExisting)).OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)target.PixelWidth,(uint)target.PixelHeight,96,96,data);
                await encoder.FlushAsync();
            }
        }
    }
}
