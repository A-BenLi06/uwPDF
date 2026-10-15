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
        public async void RunZoomQualityQa(bool runHeavy)
        {
            var folder=await ApplicationData.Current.LocalFolder.GetFolderAsync("zoom-quality-qa");
            var report=new System.Text.StringBuilder();
            try
            {
                await LoadDocumentAsync(await folder.GetFileAsync("source.pdf"));
                await Task.Delay(1200);
                QaZoomAssert(!fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-1)<.005 && ZoomLabel.Text=="100%",
                    "Opening inherited whole-page fit instead of the readable 100% default");
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

                foreach(var previous in new[]{.64f,.91f})
                {
                    SetZoom(previous,false); await Task.Delay(150);
                    await LoadDocumentAsync(await folder.GetFileAsync("source.pdf")); await Task.Delay(450);
                    QaZoomAssert(!fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-1)<.005 && DocumentScroller.VerticalOffset<1,
                        "Opening from "+previous+" failed to start directly at 100%");
                    ScheduleRelayout(); await Task.Delay(450);
                    QaZoomAssert(Math.Abs(DocumentScroller.ZoomFactor-1)<.005,"Late window/layout work changed initial zoom");
                }
                view=pageViews[0];
                report.AppendLine("PASS: native opens from previous 64/91% reset to 100%, begin at page top and stay there through deferred window/layout updates.");

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
                var usage100 = Windows.System.MemoryManager.AppMemoryUsage;
                ulong peak400 = usage100;
                var memorySampler = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                memorySampler.Tick += (sender,args) => { peak400 = Math.Max(peak400,Windows.System.MemoryManager.AppMemoryUsage); };
                memorySampler.Start();
                try
                {
                    SetZoom(4,true); await Task.Delay(1500);
                    for(var i=0;i<100 && (view.NeedsRender(ComputeRenderTargetWidth(view)) || FindNextDetailPage()!=null);i++) await Task.Delay(50);
                    QaZoomAssert(view.RenderedWidth<=1025,"400% retained a full-page high-resolution raster");
                    QaZoomAssert(view.DetailRaster!=null,"400% has no visible detail patch");
                    QaZoomAssert(view.DetailRaster.Bytes<=16000000,"400% patch exceeds bounded CPU/GPU allocation");
                    var requests=rasterBackend.RenderRequestCount;
                    await Task.Delay(1000);
                    QaZoomAssert(rasterBackend.RenderRequestCount==requests,"Idle high zoom repeatedly renders capped resolution");
                    for(var i=0;i<8;i++)
                    {
                        DocumentScroller.ChangeView(null,Math.Min(DocumentScroller.ScrollableHeight,i*110),null,false);
                        await Task.Delay(220);
                    }
                    await Task.Delay(900);
                    QaZoomAssert(cachedBitmapBytes+retiredRasters.Bytes<=activeBitmapCacheBudget,"400% exceeds retained raster budget");
                    var afterScrollRequests=rasterBackend.RenderRequestCount;
                    await Task.Delay(900);
                    QaZoomAssert(rasterBackend.RenderRequestCount==afterScrollRequests,"Settled scroll entered a redraw loop");
                    report.AppendLine("400%: usage-before="+usage100+", usage-settled="+Windows.System.MemoryManager.AppMemoryUsage+", peak="+peak400+", overview="+view.RenderedWidth+", raster-bytes="+cachedBitmapBytes+", scroll-renders="+(afterScrollRequests-requests));
                }
                finally { memorySampler.Stop(); }
                FitToWindow(true); await Task.Delay(900);
                report.AppendLine("After fit: usage="+Windows.System.MemoryManager.AppMemoryUsage+", raster-bytes="+cachedBitmapBytes);
                report.AppendLine("PASS: native 400% uses bounded overview/detail, no idle redraw loop, scroll allocations stay within raster budget.");

                var originalDpi=dpiScale; var originalZoom=DocumentScroller.ZoomFactor;
                RefreshDisplayScale(originalDpi*1.5); await Task.Delay(900);
                expected=ComputeRenderTargetWidth(view);
                for(var i=0;i<100 && view.NeedsRender(expected);i++) await Task.Delay(40);
                QaZoomAssert(!view.NeedsRender(expected) && Math.Abs(DocumentScroller.ZoomFactor-originalZoom)<.005,"DPI change failed to refine without changing zoom");
                RefreshDisplayScale(originalDpi);
                report.AppendLine("PASS: display scale update rerenders to the new device-pixel target while preserving zoom.");
                var openingFrames=new List<float>();
                EventHandler<object> openingFrame=(sender,args)=>
                {
                    if(currentFile!=null && currentFile.Name=="navigation.pdf" && document!=null && pageViews.Count>0 && pageViews[0].IsRendered)
                        openingFrames.Add(DocumentScroller.ZoomFactor);
                };
                Windows.UI.Xaml.Media.CompositionTarget.Rendering+=openingFrame;
                try { await LoadDocumentAsync(await folder.GetFileAsync("navigation.pdf")); await Task.Delay(800); }
                finally { Windows.UI.Xaml.Media.CompositionTarget.Rendering-=openingFrame; }
                QaZoomAssert(!fitPageToViewport && Math.Abs(DocumentScroller.ZoomFactor-1)<.005,"New document did not reset a previous fit: zoom="+
                    DocumentScroller.ZoomFactor+", fit="+fitPageToViewport+", document="+(document!=null)+", title="+FileNameText.Text);
                QaZoomAssert(openingFrames.Count>0 && openingFrames.TrueForAll(z=>Math.Abs(z-1)<.005),
                    "Visible new-document frames flashed an old zoom: "+string.Join(",",openingFrames));
                report.AppendLine("PASS: native composition frames of the newly rendered PDF remain at 100% while opening from fit.");
                FitToWindow(false); await Task.Delay(350);
                var navigationFrames=0;
                EventHandler<ScrollViewerViewChangedEventArgs> navigationChanged=(sender,args)=> { if(args.IsIntermediate) navigationFrames++; };
                DocumentScroller.ViewChanged+=navigationChanged;
                try
                {
                    // Start with a cold target so renderer work competes with the
                    // animation, then traverse a differently sized page and back.
                    foreach(var targetPage in new uint[]{2,1,0})
                    {
                        navigationFrames=0;
                        GoToPage(targetPage);
                        await Task.Delay(1300);
                        QaZoomAssert(navigationFrames>0,"Fit-page navigation lost native intermediate frames");
                        QaZoomAssert(Math.Abs(DocumentScroller.VerticalOffset-Math.Min(DocumentScroller.ScrollableHeight,TopOfPage(targetPage,DocumentScroller.ZoomFactor)))<2,
                            "Animated navigation did not reach requested page");
                    }
                }
                finally { DocumentScroller.ViewChanged-=navigationChanged; }
                report.AppendLine("PASS: cold-page fit navigation emits native animation frames through mixed page sizes.");
                if(runHeavy)
                {
                    await LoadDocumentAsync(await folder.GetFileAsync("performance.pdf"));
                    await Task.Delay(900);
                    // Exercise native InkCanvas as well as raster surfaces at
                    // 400%, on the image-heavy document used by the benchmark.
                    ActivatePenInput();
                    var inkView=pageViews[0]; await LoadInkAsync(inkView);
                    var inkBuilder=new Windows.UI.Input.Inking.InkStrokeBuilder();
                    inkView.Strokes.AddStroke(inkBuilder.CreateStroke(new[]{new Point(90,140),new Point(220,180)}));
                    var heavyBefore=Windows.System.MemoryManager.AppMemoryUsage;
                    SetZoom(4,true); await Task.Delay(1800);
                    for(var i=0;i<100 && FindNextDetailPage()!=null;i++) await Task.Delay(50);
                    var heavy400=Windows.System.MemoryManager.AppMemoryUsage;
                    QaZoomAssert(inkView.RenderedWidth<=1025 && inkView.DetailRaster!=null,"Heavy 400% ink view did not use bounded raster layers");
                    var heavyRequests=rasterBackend.RenderRequestCount;
                    await Task.Delay(1200);
                    QaZoomAssert(rasterBackend.RenderRequestCount==heavyRequests,"Heavy document continually redraws at rest");
                    SetInkTool(InkTool.None); FitToWindow(true); await Task.Delay(800);
                    var heavyFrames=0;
                    EventHandler<ScrollViewerViewChangedEventArgs> heavyChanged=(sender,args)=> { if(args.IsIntermediate) heavyFrames++; };
                    DocumentScroller.ViewChanged+=heavyChanged;
                    try { GoToPage(3); await Task.Delay(1700); }
                    finally { DocumentScroller.ViewChanged-=heavyChanged; }
                    QaZoomAssert(heavyFrames>0,"Image-heavy cold page jump lost all animation frames");
                    QaZoomAssert(realizedPages.Count<10,"High-zoom navigation accumulated InkCanvas controls");
                    report.AppendLine("Heavy PDF: pages="+pageViews.Count+", usage-before="+heavyBefore+", usage-400-pen="+heavy400+", usage-after-fit="+Windows.System.MemoryManager.AppMemoryUsage+", raster-bytes="+cachedBitmapBytes+", navigation-frames="+heavyFrames);
                    report.AppendLine("PASS: image-heavy PDF with ink at 400%, stable idle render count and cold-page native animation.");
                }
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
