using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Graphics.Printing;
using Windows.Graphics.Printing.OptionDetails;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private PrintManager printManager;
        private PrintDocument printDocument;
        private IPrintDocumentSource printSource;
        private PdfPrintSession printSession;
        private bool preparingPrint;
        private static bool? printingSupported;
        private const string PrintRangeId="pdfPages";

        private sealed class PdfPrintSession
        {
            public StorageFolder Folder;
            public PdfDocument Pdf;
            public string Title;
            public int[] Pages;
            public int Version;
            public bool Closed, Submitting;
            public PrintTaskOptionDetails Options;
            public readonly SemaphoreSlim RenderGate=new SemaphoreSlim(1,1);
            public readonly Dictionary<int,PdfPrintVisual> Previews=new Dictionary<int,PdfPrintVisual>();
            public readonly Queue<int> PreviewOrder=new Queue<int>();
        }
        private sealed class PdfPrintVisual : IDisposable
        {
            public Canvas Page;
            public Image Image;
            public SoftwareBitmapSource Source;
            public void Dispose()
            {
                if(Image!=null) Image.Source=null;
                if(Source!=null) Source.Dispose();
                Source=null; Image=null; Page=null;
            }
        }
        private static bool IsPrintingSupported()
        {
            if(printingSupported.HasValue) return printingSupported.Value;
            try { printingSupported=PrintManager.IsSupported(); } catch { printingSupported=false; }
            return printingSupported.Value;
        }
        private void InitializePrinting()
        {
            Unloaded+=async (s,e)=>
            {
                if(printManager!=null) printManager.PrintTaskRequested-=PrintTaskRequested;
                printManager=null;
                if(printDocument!=null)
                {
                    printDocument.Paginate-=PrintPaginate;
                    printDocument.GetPreviewPage-=PrintGetPreviewPage;
                    printDocument.AddPages-=PrintAddPages;
                }
                printDocument=null; printSource=null;
                await ClosePrintSessionAsync(printSession);
            };
        }
        private void RegisterPrinting()
        {
            if(printManager!=null || !IsPrintingSupported()) return;
            printDocument=new PrintDocument(); printSource=printDocument.DocumentSource;
            printDocument.Paginate+=PrintPaginate;
            printDocument.GetPreviewPage+=PrintGetPreviewPage;
            printDocument.AddPages+=PrintAddPages;
            printManager=PrintManager.GetForCurrentView();
            printManager.PrintTaskRequested+=PrintTaskRequested;
        }
        private async void PrintCommand_Click(object sender,RoutedEventArgs e) { await ShowPrintUiAsync(); }
        private async Task ShowPrintUiAsync()
        {
            if(document==null || currentFile==null || preparingPrint || printSession!=null || savingPdf) return;
            try
            {
                if(!IsPrintingSupported()) { await ShowPrintErrorAsync(L.Get("PrintUnavailable")); return; }
                RegisterPrinting();
                preparingPrint=true; ClearInkSelection(); ClearTextSelection();
                MainCommandBar.IsEnabled=false; AnnotationToolbar.IsHitTestVisible=false; DocumentScroller.IsEnabled=false;
                try { printSession=await PreparePrintSessionAsync(); }
                finally
                {
                    preparingPrint=false;
                    MainCommandBar.IsEnabled=true; AnnotationToolbar.IsHitTestVisible=true; DocumentScroller.IsEnabled=true;
                    UpdateUi();
                }
                if(!await PrintManager.ShowPrintUIAsync()) await ClosePrintSessionAsync(printSession);
            }
            catch(OperationCanceledException) { await ClosePrintSessionAsync(printSession); }
            catch(Exception ex) { await ClosePrintSessionAsync(printSession); await ShowPrintErrorAsync(ex.Message); }
        }
        private async Task ShowPrintErrorAsync(string message)
        {
            try { await new ContentDialog { Title=L.Get("PrintErrorTitle"),Content=message,PrimaryButtonText=L.Get("Ok") }.ShowAsync(); }
            catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Print notice: "+ex.Message); }
        }

        private async Task<PdfPrintSession> PreparePrintSessionAsync()
        {
            var token=activeRenderToken; var original=currentFile;
            var session=new PdfPrintSession { Title=original.Name };
            StorageFile snapshot=null;
            try
            {
                session.Folder=await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("uwpdf-print-"+Guid.NewGuid().ToString("N"));
                var workspace=await GetAnnotationFolderAsync();
                foreach(var file in await workspace.GetFilesAsync())
                {
                    if(token!=activeRenderToken) throw new OperationCanceledException();
                    if(file.Name.StartsWith("page-",StringComparison.Ordinal)) await file.CopyAsync(session.Folder,file.Name,NameCollisionOption.ReplaceExisting);
                }
                foreach(var view in pageViews)
                {
                    if(token!=activeRenderToken) throw new OperationCanceledException();
                    if(view.Dirty) await SavePageAnnotationsAsync(view,false,session.Folder);
                }
                // Export from an isolated workspace. Printing includes unsaved
                // ink/highlights without replacing the original or adding a save.
                snapshot=await WritePdfCopyAsync(original,token,true,session.Folder);
                await snapshot.MoveAsync(session.Folder,"document.pdf");
                session.Pdf=await PdfDocument.LoadFromFileAsync(snapshot);
                if(token!=activeRenderToken || original!=currentFile) throw new OperationCanceledException();
                PdfPrintLayout.TryPages(null,(int)session.Pdf.PageCount,out session.Pages);
                return session;
            }
            catch { await DeleteTemporaryPdfAsync(snapshot); await ClosePrintSessionAsync(session); throw; }
        }

        private void PrintTaskRequested(PrintManager sender,PrintTaskRequestedEventArgs e)
        {
            var session=printSession; var source=printSource;
            if(session==null || session.Closed || source==null) return;
            var task=e.Request.CreatePrintTask(session.Title,args=>args.SetSource(source));
            var details=PrintTaskOptionDetails.GetFromPrintTaskOptions(task.Options);
            session.Options=details;
            details.CreateTextOption(PrintRangeId,L.Get("PrintPageRangeLabel"));
            details.DisplayedOptions.Add(PrintRangeId);
            details.OptionChanged+=(s,args)=>PrintOptionsChanged(session,s,args);
            task.Completed+=async (s,args)=>
            {
                try
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,async ()=>
                    {
                        await ClosePrintSessionAsync(session);
                        if(args.Completion==PrintTaskCompletion.Failed) await ShowPrintErrorAsync(L.Get("PrintFailed"));
                    });
                }
                catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Print completion: "+ex.Message); }
            };
        }
        private async void PrintOptionsChanged(PdfPrintSession session,PrintTaskOptionDetails sender,PrintTaskOptionChangedEventArgs e)
        {
            if(e.OptionId==null || e.OptionId.ToString()!=PrintRangeId || session.Closed) return;
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>
                {
                    if(session.Closed || printSession!=session || session.Submitting) return;
                    var option=sender.Options[PrintRangeId]; int[] pages;
                    if(!PdfPrintLayout.TryPages(option.Value as string,(int)session.Pdf.PageCount,out pages))
                    { option.ErrorText=L.Get("PrintPageRangeError"); return; }
                    option.ErrorText=string.Empty; session.Pages=pages;
                    printDocument.InvalidatePreview();
                });
            }
            catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Print options: "+ex.Message); }
        }
        private void ClearPrintPreviews(PdfPrintSession session)
        {
            foreach(var visual in session.Previews.Values)
            { PrintCanvas.Children.Remove(visual.Page); visual.Dispose(); }
            session.Previews.Clear(); session.PreviewOrder.Clear();
        }
        private void PrintPaginate(object sender,PaginateEventArgs e)
        {
            var session=printSession;
            if(session==null || session.Closed || session.Submitting) return;
            previewPrintOptions=e.PrintTaskOptions;
            session.Version++; ClearPrintPreviews(session);
            ((PrintDocument)sender).SetPreviewPageCount(session.Pages.Length,PreviewPageCountType.Final);
        }
        private PrintTaskOptions previewPrintOptions;
        private async void PrintGetPreviewPage(object sender,GetPreviewPageEventArgs e)
        {
            var session=printSession; var version=session==null ? -1 : session.Version;
            if(session==null || session.Closed || session.Submitting || e.PageNumber<1 || e.PageNumber>session.Pages.Length || previewPrintOptions==null) return;
            PdfPrintVisual created=null;
            try
            {
                PdfPrintVisual visual;
                if(!session.Previews.TryGetValue(e.PageNumber,out visual))
                {
                    created=await CreatePrintVisualAsync(session,session.Pages[e.PageNumber-1],previewPrintOptions.GetPageDescription(0),96);
                    if(created==null || session.Closed || version!=session.Version || session.Submitting) return;
                    if(session.Previews.TryGetValue(e.PageNumber,out visual)) { created.Dispose(); created=null; }
                    else
                    {
                        visual=created; created=null;
                        AttachPrintVisual(visual); session.Previews.Add(e.PageNumber,visual); session.PreviewOrder.Enqueue(e.PageNumber);
                    }
                }
                ((PrintDocument)sender).SetPreviewPage(e.PageNumber,visual.Page);
                while(session.PreviewOrder.Count>3)
                {
                    var key=session.PreviewOrder.Dequeue(); var old=session.Previews[key];
                    session.Previews.Remove(key); PrintCanvas.Children.Remove(old.Page); old.Dispose();
                }
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Print preview: "+ex.Message);
                if(!session.Closed && session.Options!=null) session.Options.Options[PrintRangeId].ErrorText=L.Get("PrintFailed");
            }
            finally { if(created!=null) created.Dispose(); }
        }
        private async void PrintAddPages(object sender,AddPagesEventArgs e)
        {
            var session=printSession;
            if(session==null || session.Closed || session.Submitting) return;
            session.Submitting=true; session.Version++; ClearPrintPreviews(session);
            var pages=(int[])session.Pages.Clone();
            try
            {
                foreach(var index in pages)
                {
                    using(var visual=await CreatePrintVisualAsync(session,index,e.PrintTaskOptions.GetPageDescription(0),300))
                    {
                        if(visual==null || session.Closed) return;
                        AttachPrintVisual(visual);
                        try { ((PrintDocument)sender).AddPage(visual.Page); }
                        finally { PrintCanvas.Children.Remove(visual.Page); }
                    }
                }
                ((PrintDocument)sender).AddPagesComplete();
            }
            catch(Exception ex)
            {
                // Never complete a partial document as a successful print job.
                System.Diagnostics.Debug.WriteLine("Print pages: "+ex.Message);
                if(!session.Closed && session.Options!=null) session.Options.Options[PrintRangeId].ErrorText=L.Get("PrintFailed");
            }
        }
        private void AttachPrintVisual(PdfPrintVisual visual)
        {
            PrintCanvas.Children.Add(visual.Page);
            PrintCanvas.InvalidateMeasure();
            PrintCanvas.UpdateLayout();
            visual.Page.Measure(new Size(visual.Page.Width,visual.Page.Height));
            visual.Page.Arrange(new Rect(0,0,visual.Page.Width,visual.Page.Height));
            visual.Page.UpdateLayout();
        }
        private async Task<PdfPrintVisual> CreatePrintVisualAsync(PdfPrintSession session,int index,PrintPageDescription description,double dpi)
        {
            await session.RenderGate.WaitAsync();
            SoftwareBitmapSource source=null;
            try
            {
                if(session.Closed || session.Pdf==null) return null;
                using(var page=session.Pdf.GetPage((uint)index))
                using(var stream=new InMemoryRandomAccessStream())
                {
                    var area=description.ImageableRect;
                    var layout=PdfPrintLayout.Fit(page.Size.Width,page.Size.Height,description.PageSize.Width,description.PageSize.Height,
                        area.X,area.Y,area.Width,area.Height,dpi);
                    await page.RenderToStreamAsync(stream,new PdfPageRenderOptions { DestinationWidth=layout.PixelWidth,DestinationHeight=layout.PixelHeight,BitmapEncoderId=BitmapEncoder.BmpEncoderId });
                    if(session.Closed) return null;
                    stream.Seek(0); var decoder=await BitmapDecoder.CreateAsync(stream);
                    using(var pixels=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,
                        new BitmapTransform { ScaledWidth=layout.PixelWidth,ScaledHeight=layout.PixelHeight },ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage))
                    {
                        source=new SoftwareBitmapSource(); await source.SetBitmapAsync(pixels);
                    }
                    if(session.Closed) return null;
                    var image=new Image { Source=source,Width=layout.Width,Height=layout.Height,Stretch=Stretch.Fill };
                    var root=new Canvas { Width=description.PageSize.Width,Height=description.PageSize.Height,Background=new SolidColorBrush(Colors.White) };
                    Canvas.SetLeft(image,layout.X); Canvas.SetTop(image,layout.Y); root.Children.Add(image);
                    var visual=new PdfPrintVisual { Page=root,Image=image,Source=source }; source=null;
                    return visual;
                }
            }
            finally { if(source!=null) source.Dispose(); session.RenderGate.Release(); }
        }
        private async Task ClosePrintSessionAsync(PdfPrintSession session)
        {
            if(session==null || session.Closed) return;
            session.Closed=true; session.Version++; ClearPrintPreviews(session);
            await session.RenderGate.WaitAsync();
            try { session.Pdf=null; session.Options=null; }
            finally { session.RenderGate.Release(); }
            if(printSession==session) { printSession=null; previewPrintOptions=null; UpdateUi(); }
            if(session.Folder!=null)
            {
                try { await session.Folder.DeleteAsync(); }
                catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Print workspace cleanup: "+ex.Message); }
                session.Folder=null;
            }
        }
    }
}
