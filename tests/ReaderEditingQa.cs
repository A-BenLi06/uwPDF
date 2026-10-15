using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        // Compiled only by Test-ReaderEditing.ps1. This exercises our own page,
        // native InkStrokeContainer, storage and OCR without desktop automation.
        public async void RunReaderEditingQa()
        {
            var output = await ApplicationData.Current.LocalFolder.GetFolderAsync("reader-editing-qa");
            var report = new System.Text.StringBuilder();
            try
            {
                var source = await output.GetFileAsync("source.pdf");
                var fixture = await source.CopyAsync(output, "edit-" + Guid.NewGuid().ToString("N") + ".pdf");
                await LoadDocumentAsync(fixture); await Task.Delay(700);
                var view = pageViews[0]; await LoadInkAsync(view);
                var builder = new InkStrokeBuilder(); builder.SetDefaultDrawingAttributes(CreateInkAttributes());
                var stroke = builder.CreateStroke(new[] { new Point(100, 190), new Point(220, 190) });
                view.EnsureStrokes().AddStroke(stroke); view.UndoKinds.Add(0); MarkPageDirty(view);
                SetInkTool(InkTool.Pen);
                QaEditingAssert(view.InkLayer.InkPresenter.InputProcessingConfiguration.RightDragAction == InkInputRightDragAction.LeaveUnprocessed, "Native barrel routing");
                SetInkTool(InkTool.InkSelect);
                QaEditingAssert(view.InkSelectionLayer.Background != null && InkSelectButton.IsEnabled,
                    "Mouse selection has an exposed toolbar button and a full-page hit target");
                var emptyPoint = view.InkSelectionLayer.TransformToVisual(null).TransformPoint(new Point(280,100));
                QaEditingAssert(VisualTreeHelper.FindElementsInHostCoordinates(emptyPoint,view.Host).Contains(view.InkSelectionLayer),
                    "Selection overlay participates in native empty-page hit testing");
                SetAnnotationToolbarOpen(true); TopSelectButton_Click(null,null);
                QaEditingAssert(currentInkTool==InkTool.None && AnnotationToolbar.Visibility==Visibility.Collapsed &&
                    view.SelectionLayer.IsHitTestVisible && view.InkSelectionLayer.Background==null,
                    "Text selection collapses toolbar and restores cursor/touch selection");
                ActivatePenInput();
                QaEditingAssert(currentInkTool==InkTool.Pen && AnnotationToolbar.Visibility==Visibility.Visible && !view.SelectionLayer.IsHitTestVisible,
                    "Detected pen automatically restores annotation mode and tools");
                QaEditingAssert((view.InkLayer.InkPresenter.InputDeviceTypes & Windows.UI.Core.CoreInputDeviceTypes.Touch)==0,
                    "Touch never becomes ink input");
                var outsideInk=builder.CreateStroke(new[]{new Point(200,240),new Point(220,240)});
                view.Strokes.AddStroke(outsideInk);
                BeginInkGesture(view,new Point(80,170),88);
                foreach(var point in new[]{new Point(250,170),new Point(250,210),new Point(160,210),new Point(160,270),new Point(80,270)})
                    UpdateInkGesture(view,point,88);
                QaEditingAssert(inkLasso!=null && inkLasso.Points.Count>=6,"Freehand path is visible during cursor/pen selection");
                FinishInkGesture(view,new Point(80,180),88);
                QaEditingAssert(stroke.Selected && !outsideInk.Selected && inkLasso==null,
                    "Native concave lasso excludes ink inside its bounding rectangle but outside the loop; open end closes on release");
                stroke.Selected=false; outsideInk.Selected=true; view.Strokes.DeleteSelected(); ClearInkSelection();
                BeginInkGesture(view,new Point(30,30),89); UpdateInkGesture(view,new Point(45,55),89); CancelInkGesture();
                QaEditingAssert(inkLasso==null && !stroke.Selected,"Canceled lasso removes its temporary path without selecting ink");
                var originalTransform = stroke.PointTransform;
                QaLassoInk(view, 90);
                QaEditingAssert(stroke.Selected && selectedInkPage == view, "Freehand ellipse selects native ink");
                QaEditingAssert(inkSelectionRectangle.Height * DocumentScroller.ZoomFactor >= 12, "Thin ink has a usable selection target");
                var bounds = inkSelectionBounds; var start = new Point(bounds.X + bounds.Width / 2, bounds.Y - 3 / DocumentScroller.ZoomFactor);
                BeginInkGesture(view, start, 91); FinishInkGesture(view, new Point(start.X + 38, start.Y + 29), 91);
                QaEditingAssert(Math.Abs(stroke.PointTransform.M31 - originalTransform.M31 - 38) < .01, "Moved ink transform");
                UndoButton_Click(null, null);
                QaEditingAssert(stroke.PointTransform == originalTransform && view.Strokes.GetStrokes().Count == 1, "Undo moves without deleting selected ink");
                QaLassoInk(view, 92);
                bounds = inkSelectionBounds; start = new Point(bounds.X + 10, bounds.Y + 1);
                BeginInkGesture(view, start, 93); UpdateInkGesture(view, new Point(start.X + 40, start.Y + 35), 93); CancelInkGesture();
                QaEditingAssert(stroke.PointTransform == originalTransform, "Canceled move restores exact transform");
                ClearInkSelection();
                QaLassoInk(view, 94);
                bounds = inkSelectionBounds; start = new Point(bounds.X + 10, bounds.Y + 1);
                BeginInkGesture(view, start, 95); FinishInkGesture(view, new Point(start.X + 38, start.Y + 29), 95);
                var moved = stroke.PointTransform;
                await SaveCurrentAnnotationsAsync();
                var annotations = await GetAnnotationFolderAsync(); var annotationPath = annotations.Path;
                var entries = await PdfSnapshotStore.ListAsync(annotations);
                QaEditingAssert(entries.Count == 2 && !HasUnsavedChanges(), "First save creates baseline and saved snapshot");
                var first = entries.First(e => e.Kind == "save"); var baseline = entries.First(e => e.Kind == "baseline");
                HistoryButton_Click(null, null);
                for (var i = 0; i < 100 && historyDialog == null; i++) await Task.Delay(30);
                await Task.Delay(600);
                QaEditingAssert(historyDialog != null && historyDialog.Content is ScrollViewer, "Native adaptive history dialog");
                await QaEditingBitmapAsync(historyDialog, output, "history-preview.png");
                historyDialog.Hide(); await Task.Delay(300);
                QaEditingAssert(!historyOpen, "History closes without modifying document");
                var originalPdf = await QaSnapshotPageAsync(await baseline.Folder.GetFileAsync("document.pdf"));
                var savedPdf = await QaSnapshotPageAsync(await first.Folder.GetFileAsync("document.pdf"));
                QaEditingAssert(!Windows.Security.Cryptography.CryptographicBuffer.Compare(originalPdf, savedPdf), "Native snapshot preview renders embedded ink appearance");
                var savedStrokes = new InkStrokeContainer();
                using (var input = await (await annotations.GetFileAsync(GetInkFileName(0))).OpenSequentialReadAsync()) await savedStrokes.LoadAsync(input);
                QaEditingAssert(savedStrokes.GetStrokes().Count == 1 && savedStrokes.GetStrokes()[0].PointTransform == moved, "Saved move remains editable");
                view.Strokes.AddStroke(builder.CreateStroke(new[] { new Point(320, 200), new Point(400, 230) })); MarkPageDirty(view);
                await SaveCurrentAnnotationsAsync();
                QaEditingAssert((await PdfSnapshotStore.ListAsync(annotations)).Count == 3, "Second save adds independent snapshot");
                await RestoreSnapshotAndReloadAsync(first); await Task.Delay(500);
                view = pageViews[0]; await LoadInkAsync(view);
                QaEditingAssert((await GetAnnotationFolderAsync()).Path == annotationPath, "Restore retains document annotation identity");
                QaEditingAssert(view.Strokes.GetStrokes().Count == 1 && view.Strokes.GetStrokes()[0].PointTransform == moved, "Restore saves ink geometry and editability");
                QaEditingAssert(MainCommandBar.IsEnabled && DocumentScroller.IsEnabled, "Restore releases busy state");
                await RestoreSnapshotAndReloadAsync(baseline); await Task.Delay(400);
                view = pageViews[0]; await LoadInkAsync(view);
                QaEditingAssert(view.Strokes.GetStrokes().Count == 0, "Baseline restores original PDF without ink");
                await RestoreSnapshotAndReloadAsync(first); await Task.Delay(400);
                var before = await FileIO.ReadBufferAsync(currentFile);
                var badPdf = await first.Folder.GetFileAsync("document.pdf"); await badPdf.RenameAsync("unavailable.pdf");
                var failed = false;
                try { await RestoreSnapshotAndReloadAsync(first); }
                catch (Exception) { failed = true; }
                finally { await badPdf.RenameAsync("document.pdf"); }
                var after = await FileIO.ReadBufferAsync(currentFile);
                QaEditingAssert(failed && Windows.Security.Cryptography.CryptographicBuffer.Compare(before, after), "Failed restore preserves original PDF");
                view = pageViews[0]; await LoadInkAsync(view);
                QaEditingAssert(view.Strokes.GetStrokes().Count == 1 && MainCommandBar.IsEnabled && DocumentScroller.IsEnabled && document != null, "Failed restore rolls back sidecars and reloads reader");
                report.AppendLine("PASS: exposed mouse selection target, native hit testing, pen auto mode and toolbar dismissal; native freehand/concave lasso selection, move, cancel, undo, save; immutable baseline/saves, editable restore and failure rollback; no desktop input injection.");

                await EnsurePageTextAsync(view, activeRenderToken);
                SetInkTool(InkTool.None);
                QaEditingAssert(view.Glyphs != null && view.Glyphs.Count > 0, "Native text available");
                var glyphIndex = view.Glyphs.FindIndex(g => g.Width > 0 && g.Height > 0 && !string.IsNullOrWhiteSpace(g.Text));
                var glyph = view.Glyphs[glyphIndex];
                var center = new Point((glyph.TopLeft.X + glyph.BottomRight.X) / 2 * view.LayoutWidth, (glyph.TopLeft.Y + glyph.BottomRight.Y) / 2 * view.LayoutHeight);
                QaEditingAssert(HitGlyph(view, center, false) == glyphIndex, "Native glyph quad hit");
                foreach (var factor in new[] { .64f, .8f, 1f, 1.6f })
                {
                    SetZoom(factor, false); await Task.Delay(150);
                    var slop = 8 / factor;
                    var outside = new Point(glyph.X * view.LayoutWidth - 5 / factor, glyph.Y * view.LayoutHeight);
                    QaEditingAssert(HitGlyph(view, outside, true, slop) >= 0, "Pen hit slop at " + factor);
                    QaEditingAssert(HitGlyph(view, new Point(-100, -100), true, slop) < 0, "Pen rejects distant empty area");
                }
                selectedTextPage = view; selectionAnchor = selectionEnd = glyphIndex; selectionPointer = null;
                RedrawTextSelection(view);
                QaEditingAssert(view.SelectionLayer.Children.OfType<Grid>().Count() == 2 && view.SelectionLayer.Children.OfType<Grid>().All(h => h.ManipulationMode == Windows.UI.Xaml.Input.ManipulationModes.None), "Two screen-sized selection handles");
                report.AppendLine("PASS: native text quad geometry at 64/80/100/160%, whitespace rejection and selection handles (pen input is annotation-only).");
                ClearTextSelection();

                var scanned = await output.GetFileAsync("scanned.pdf");
                var languages = OcrEngine.AvailableRecognizerLanguages;
                report.AppendLine("Windows OCR languages: " + string.Join(", ", languages.Select(l => l.LanguageTag)));
                QaEditingAssert(languages.Count > 0, "An installed OCR language is required for native OCR QA");
                var language = languages.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? languages[0];
                var ocr = new PdfOcrSource(scanned, language.LanguageTag);
                var recognized = await ocr.ReadAsync(0, CancellationToken.None);
                var words = string.Join("", recognized.Select(g => g.Text)); report.AppendLine("OCR result: " + words);
                QaEditingAssert(recognized.Any(g => g.FromOcr && g.Text.IndexOf("recognition", StringComparison.OrdinalIgnoreCase) >= 0), "Windows OCR recognizes raster PDF");
                QaEditingAssert(recognized.Where(g => g.FromOcr).All(g => g.Width > 0 && g.Height > 0 && g.X >= 0 && g.Y >= 0 && g.X + g.Width <= 1.01 && g.Y + g.Height <= 1.01), "OCR word coordinates are normalized to rendered page");
                var nativeSource = new PdfTextSource(scanned);
                QaEditingAssert((await nativeSource.ReadPageAsync(0)).Count == 0, "Scanned fixture has no native text layer"); await nativeSource.DisposeAsync();
                var fallbackSource = new PdfTextSource(scanned, ocr.ReadAsync);
                QaEditingAssert((await fallbackSource.ReadPageAsync(0)).Any(g => g.FromOcr), "OCR fallback feeds normal text source"); await fallbackSource.DisposeAsync();
                ocrLanguage = language.LanguageTag;
                await LoadDocumentAsync(scanned); await Task.Delay(500); ShowSearch(); SearchTextBox.Text = "recognition";
                await Task.Delay(350);
                for (var i = 0; i < 300 && searchBusy; i++) await Task.Delay(30);
                if (!searchIndexComplete) await FindTextAsync(true, true);
                report.AppendLine("OCR search: hits=" + searchHits.Count + ", status=" + SearchStatus.Text + ", busy=" + searchBusy);
                QaEditingAssert(searchHits.Count == 2 && activeSearchHit != null, "OCR participates in full-document search");
                await FindTextAsync(true, false); QaEditingAssert(searchHitIndex == 1, "OCR search next match");
                CloseSearch_Click(null, null);
                view = pageViews[0]; await EnsurePageTextAsync(view, activeRenderToken);
                var hit = view.Glyphs.FindIndex(g => g.FromOcr && g.Text.IndexOf("recognition", StringComparison.OrdinalIgnoreCase) >= 0);
                await ScrollToSearchMatchAsync(pageTops[0] + view.Glyphs[hit].Y * view.LayoutHeight, CancellationToken.None);
                selectedTextPage = view; selectionAnchor = selectionEnd = hit; RedrawTextSelection(view);
                QaEditingAssert(SelectedText().IndexOf("recognition", StringComparison.OrdinalIgnoreCase) >= 0, "OCR text copy selection");
                await QaEditingBitmapAsync(MainChrome, output, "ocr-selection.png");
                report.AppendLine("PASS: Windows.Media.Ocr reads raster PDF; bounded normalized word quads feed automatic fallback, page selection and animated document search (2 of 2).");
                OcrButton_Click(null, null); await Task.Delay(400);
                QaEditingAssert(ocrDialog != null && ocrDialog.Content is ScrollViewer, "Native OCR language dialog");
                await QaEditingBitmapAsync(ocrDialog, output, "ocr-language-dialog.png");
                ocrDialog.Hide();
                // Hide completes after the native closing animation; a fixed
                // 200 ms delay races its ShowAsync continuation under load.
                for(var i=0;i<100 && ocrDialogOpen;i++) await Task.Delay(30);
                QaEditingAssert(!ocrDialogOpen, "OCR dialog cancellation");
                await QaCrossPageSelectionAsync(output,report);
                report.AppendLine("PASS: all reader editing QA completed. Physical barrel-button and touch input remain device acceptance checks.");
            }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex); }
            await FileIO.WriteTextAsync(await output.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting), report.ToString());
        }
        private async Task QaCrossPageSelectionAsync(StorageFolder output,System.Text.StringBuilder report)
        {
            var fixture=await (await output.GetFileAsync("selection.pdf")).CopyAsync(output,"selection-"+Guid.NewGuid().ToString("N")+".pdf");
            await LoadDocumentAsync(fixture); await Task.Delay(800);
            SetInkTool(InkTool.None); ClearTextSelection();
            var version=textSelectionVersion; var token=activeRenderToken;
            var firstPage=pageViews[0]; var fourthPage=pageViews[3];
            await EnsureSelectionTextAsync(firstPage,version,token); await EnsureSelectionTextAsync(fourthPage,version,token);
            selectedTextPage=firstPage; selectionAnchor=0; selectionEndPage=fourthPage; selectionEnd=fourthPage.Glyphs.Count-1;
            var copied=await SelectedTextAsync();
            QaEditingAssert(copied.IndexOf("PAGE_ONE")>=0 && copied.IndexOf("PAGE_TWO")>copied.IndexOf("PAGE_ONE") &&
                copied.IndexOf("PAGE_THREE")>copied.IndexOf("PAGE_TWO") && copied.IndexOf("PAGE_FOUR")>copied.IndexOf("PAGE_THREE"),"Forward cross-page copy retains reading order");
            selectedTextPage=fourthPage; selectionAnchor=fourthPage.Glyphs.Count-1; selectionEndPage=firstPage; selectionEnd=0;
            QaEditingAssert(await SelectedTextAsync()==copied,"Reverse cross-page copy retains reading order");
            VirtualizePage(firstPage);
            QaEditingAssert(selectedTextPage==fourthPage && SelectionEndpoint==firstPage && firstPage.Glyphs!=null,"Endpoint virtualization discarded cross-page selection");
            RealizePage(firstPage);
            selectedTextPage=firstPage; selectionAnchor=0; selectionEndPage=null; selectionEnd=firstPage.Glyphs.Count-1;
            RedrawTextSelection(firstPage);
            var polygons=firstPage.SelectionPolygons.ToArray(); var children=firstPage.SelectionLayer.Children.Count;
            selectionPointer=881; // Own state only: no synthetic hardware events.
            for(var i=0;i<10;i++) { selectionEnd=i%2==0 ? firstPage.Glyphs.Count-1 : firstPage.Glyphs.Count/2; RedrawTextSelection(firstPage); }
            var gcBefore=GC.CollectionCount(0);
            for(var i=0;i<500;i++) { selectionEnd=i%2==0 ? firstPage.Glyphs.Count-1 : firstPage.Glyphs.Count/2; RedrawTextSelection(firstPage); }
            var gcAfter=GC.CollectionCount(0);
            QaEditingAssert(firstPage.SelectionLayer.Children.Count==children && polygons.SequenceEqual(firstPage.SelectionPolygons),"Drag selection recreated XAML shapes/handles after warmup");
            selectionPointer=null;
            SetZoom(.8f,false); await Task.Delay(400);
            var beforeScroll=DocumentScroller.VerticalOffset;
            selectionViewportPoint=new Point(DocumentScroller.ViewportWidth*.5,DocumentScroller.ViewportHeight+60);
            selectionLastTick=DateTime.UtcNow; selectionPointer=882;
            UpdateSelectionScroll(); await Task.Delay(1000); StopSelectionScroll(); selectionPointer=null;
            QaEditingAssert(DocumentScroller.VerticalOffset>beforeScroll+100 && SelectionEndpoint.Index>0,"Edge autoscroll did not advance selection across a page");
            QaEditingAssert(PdfSelectionScroll.Speed(0,600,2)>0==false &&
                Math.Abs(PdfSelectionScroll.Speed(0,600,2))>Math.Abs(PdfSelectionScroll.Speed(0,600,0)) && PdfSelectionScroll.Speed(300,600,3)==0,
                "Edge dwell acceleration/centre stop policy");
            // A multi-page highlight must preserve both endpoint ranges and the
            // pre-existing annotation state on every participating page.
            ClearTextSelection(); version=textSelectionVersion;
            await EnsureSelectionTextAsync(firstPage,version,token); await EnsureSelectionTextAsync(pageViews[1],version,token);
            selectedTextPage=firstPage; selectionAnchor=0; selectionEndPage=pageViews[1]; selectionEnd=pageViews[1].Glyphs.Count-1;
            await HighlightSelectionAsync(firstPage);
            QaEditingAssert(firstPage.Highlights.Count>0 && pageViews[1].Highlights.Count>0 && firstPage.Dirty && pageViews[1].Dirty,"Cross-page highlight failed to mark both pages");
            await SaveCurrentAnnotationsAsync();
            QaEditingAssert(dirtyCount==0,"Cross-page highlight save retained dirty pages");
            await LoadDocumentAsync(currentFile); await Task.Delay(500);
            RealizePage(pageViews[0]); RealizePage(pageViews[1]);
            await LoadInkAsync(pageViews[0]); await LoadInkAsync(pageViews[1]);
            QaEditingAssert(pageViews[0].Highlights.Count>0 && pageViews[1].Highlights.Count>0,"Saved cross-page highlights were not restored as editable annotations");
            report.AppendLine("PASS: native forward/reverse four-page copy, selection survives endpoint virtualization, stable XAML pool through 500 drag updates (Gen0 collections="+(gcAfter-gcBefore)+"), accelerating edge scroll crosses pages, two-page highlight save/reload.");
        }
        private static async Task<IBuffer> QaSnapshotPageAsync(StorageFile file)
        {
            var pdf = await PdfDocument.LoadFromFileAsync(file);
            using (var page = pdf.GetPage(0))
            using (var stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 300, DestinationHeight = 420 });
                stream.Seek(0); var decoder = await BitmapDecoder.CreateAsync(stream);
                var pixels = await decoder.GetPixelDataAsync();
                return Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels.DetachPixelData());
            }
        }
        private void QaLassoInk(PageView view,uint pointer)
        {
            var start=new Point(260,192.5);
            BeginInkGesture(view,start,pointer);
            for(var i=1;i<32;i++)
            {
                var angle=i*Math.PI*2/32;
                UpdateInkGesture(view,new Point(155+105*Math.Cos(angle),192.5+42.5*Math.Sin(angle)),pointer);
            }
            FinishInkGesture(view,start,pointer);
        }
        private static void QaEditingAssert(bool value, string label) { if (!value) throw new Exception(label); }
        private static async Task QaEditingBitmapAsync(FrameworkElement element, StorageFolder folder, string name)
        {
            var target = new RenderTargetBitmap(); await target.RenderAsync(element);
            var pixels = await target.GetPixelsAsync();
            using (var stream = await (await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting)).OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                var reader = DataReader.FromBuffer(pixels); var data = new byte[pixels.Length]; reader.ReadBytes(data);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, data);
                await encoder.FlushAsync();
            }
        }
    }
}
