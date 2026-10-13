using System;
using System.Globalization;
using System.Threading.Tasks;
using PdfNative;
#if CPPWINRT_RENDERER
using InkOutlineExporter = PdfNative.Rendering.InkOutlineExporter;
using PdfAnnotationWriter = PdfNative.Rendering.PdfAnnotationWriter;
#endif
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using System.Collections.Generic;
using Windows.Data.Pdf;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private Task saveInProgress;
        private bool savingPdf;
        private StorageFile readingCopy;
        private string annotationOwner;
        private readonly HashSet<uint> embeddedPages = new HashSet<uint>();

        private static JsonArray Numbers(params double[] values)
        {
            var array = new JsonArray();
            foreach (var n in values) array.Add(JsonValue.CreateNumberValue(n));
            return array;
        }
        private JsonObject ExportItem(uint page, string kind, Color color, double opacity)
        {
            var item = new JsonObject();
            item["owner"] = JsonValue.CreateStringValue(annotationOwner);
            item["page"] = JsonValue.CreateNumberValue(page);
            item["kind"] = JsonValue.CreateStringValue(kind);
            item["color"] = Numbers(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            item["opacity"] = JsonValue.CreateNumberValue(opacity * color.A / 255.0);
            return item;
        }

        private async Task AppendExportAnnotationsAsync(PdfAnnotationWriter writer, ulong token)
        {
            // Enumerate only actual annotation files.
            // Saving does not load every page into the viewer's caches.
            if (token != activeRenderToken) throw new OperationCanceledException();
            var layoutWidth = ComputeColumnWidth();
            if (token != activeRenderToken) throw new OperationCanceledException();
            var folder = await GetAnnotationFolderAsync();
            InkOutlineExporter outlineExporter = null;
            try
            {
                foreach (var file in await folder.GetFilesAsync())
                {
                    if (token != activeRenderToken) throw new OperationCanceledException();
                    if (!file.Name.StartsWith("page-", StringComparison.Ordinal)) continue;
                    var end = file.Name.IndexOfAny(new[] { '-', '.' }, 5);
                    uint index;
                    if (end < 0 || !uint.TryParse(file.Name.Substring(5, end - 5), out index) || index >= pageViews.Count) continue;
                    if (file.Name.EndsWith("-highlights.json", StringComparison.Ordinal))
                    {
                        var saved = JsonArray.Parse(await FileIO.ReadTextAsync(file));
                        foreach (var value in saved)
                        {
                            if (token != activeRenderToken) throw new OperationCanceledException();
                            var highlight = value.GetObject();
                            var color = ParseHexColor(highlight.GetNamedString("color"));
                            var quads = highlight.GetNamedArray("quads", new JsonArray());
                            foreach (var r in highlight.GetNamedArray("rectangles", new JsonArray()))
                            {
                                var a = r.GetArray();
                                var x = a[0].GetNumber(); var y = a[1].GetNumber(); var w = a[2].GetNumber(); var h = a[3].GetNumber();
                                quads.Add(Numbers(x, y, x + w, y, x, y + h, x + w, y + h));
                            }
                            if (quads.Count == 0) continue;
                            var item = ExportItem(index, "highlight", color, 0.35);
                            item["quads"] = quads;
                            await writer.AppendAsync(new JsonArray { item }.Stringify());
                        }
                    }
                    else if (file.FileType.Equals(".isf", StringComparison.OrdinalIgnoreCase))
                    {
                        var strokes = new InkStrokeContainer();
                        using (var input = await file.OpenSequentialReadAsync()) await strokes.LoadAsync(input);
                        if (token != activeRenderToken) throw new OperationCanceledException();
                        // A saved page may never have been visited in this
                        // session. Use actual PDF geometry, not its placeholder.
                        var size = await writer.ReadPageSizeAsync(index);
                        if (token != activeRenderToken) throw new OperationCanceledException();
                        var layoutHeight = layoutWidth * size.Height / size.Width;
                        foreach (var stroke in strokes.GetStrokes())
                        {
                            if (token != activeRenderToken) throw new OperationCanceledException();
                            if (outlineExporter == null) outlineExporter = await InkOutlineExporter.CreateAsync();
                            var attributes = stroke.DrawingAttributes;
                            var item = ExportItem(index, "ink", attributes.Color, attributes.DrawAsHighlighter ? 0.35 : 1);
                            item["width"] = JsonValue.CreateNumberValue(attributes.Size.Width / layoutWidth);
                            var points = new JsonArray();
                            var transform = stroke.PointTransform;
                            foreach (var p in stroke.GetInkPoints())
                            {
                                var x = p.Position.X * transform.M11 + p.Position.Y * transform.M21 + transform.M31;
                                var y = p.Position.X * transform.M12 + p.Position.Y * transform.M22 + transform.M32;
                                points.Add(Numbers(x / layoutWidth, y / layoutHeight));
                            }
                            item["points"] = points;
                            if (points.Count > 0)
                            {
                                // Capture the system-rendered outline, including curves,
                                // pressure, pen tip shape and the stroke transform.
                                var appearance = JsonObject.Parse(await outlineExporter.DescribeAsync(stroke, layoutWidth, layoutHeight));
                                foreach (var pair in appearance) item[pair.Key] = pair.Value;
                                item["highlighter"] = JsonValue.CreateBooleanValue(attributes.DrawAsHighlighter);
                                if (token != activeRenderToken) throw new OperationCanceledException();
                                // Append one annotation at a time. Never retain a whole
                                // document's expanded outline JSON in the managed heap.
                                await writer.AppendAsync(new JsonArray { item }.Stringify());
                            }
                        }
                        strokes.Clear();
                    }
                }
                if (token != activeRenderToken) throw new OperationCanceledException();
            }
            finally { if (outlineExporter != null) outlineExporter.Dispose(); }
        }

        private string ClearEmbeddedAnnotations()
        {
            var items = new JsonArray();
            foreach (var page in embeddedPages)
            {
                if (document != null && page >= document.PageCount) continue;
                var item = new JsonObject();
                item["page"] = JsonValue.CreateNumberValue(page);
                item["kind"] = JsonValue.CreateStringValue("clear");
                item["owner"] = JsonValue.CreateStringValue(annotationOwner);
                items.Add(item);
            }
            return items.Stringify();
        }

        private async Task<StorageFile> WritePdfCopyAsync(StorageFile original, ulong token, bool includeAnnotations)
        {
            StorageFile staging = null;
            try
            {
                using (var input = await original.OpenReadAsync())
                {
                    var writer = await PdfAnnotationWriter.OpenAsync(input);
                    try
                    {
                        await writer.AppendAsync(ClearEmbeddedAnnotations());
                        if (includeAnnotations) await AppendExportAnnotationsAsync(writer, token);
                        if (token != activeRenderToken) throw new OperationCanceledException();
                        staging = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("uwpdf-save-" + Guid.NewGuid().ToString("N") + ".pdf");
                        using (var output = await staging.OpenAsync(FileAccessMode.ReadWrite)) await writer.WriteAsync(output);
                    }
                    finally
                    {
                        // Release native document/font stores off the UI thread,
                        // before closing the caller's input stream.
                        await Task.Run(() => writer.Dispose());
                    }
                }
                if (token != activeRenderToken) throw new OperationCanceledException();
                return staging;
            }
            catch
            {
                await DeleteTemporaryPdfAsync(staging);
                throw;
            }
        }

        private static async Task DeleteTemporaryPdfAsync(StorageFile file)
        {
            if (file == null) return;
            try { await file.DeleteAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Temporary PDF cleanup: " + ex.Message); }
        }

        private async Task<StorageFile> PrepareReadingFileAsync(StorageFile original, ulong token)
        {
            annotationOwner = Guid.NewGuid().ToString("N");
            embeddedPages.Clear();
            var folder = await GetAnnotationFolderAsync();
            if (token != activeRenderToken) throw new OperationCanceledException();
            var stateFile = await folder.TryGetItemAsync("pdf-state.json") as StorageFile;
            if (stateFile == null) return original;
            var state = JsonObject.Parse(await FileIO.ReadTextAsync(stateFile));
            if (token != activeRenderToken) throw new OperationCanceledException();
            annotationOwner = state.GetNamedString("owner");
            foreach (var page in state.GetNamedArray("pages")) embeddedPages.Add((uint)page.GetNumber());
            var copy = await WritePdfCopyAsync(original, token, false);
            if (token != activeRenderToken) { await DeleteTemporaryPdfAsync(copy); throw new OperationCanceledException(); }
            readingCopy = copy;
            return copy;
        }

        private async Task SavePdfAnnotationsAsync()
        {
            if (currentFile == null || document == null) return;
            savingPdf = true;
            MainCommandBar.IsEnabled = false;
            AnnotationToolbar.IsHitTestVisible = false;
            DocumentScroller.IsEnabled = false;
            StorageFile staging = null;
            var backups = new Dictionary<string, StorageFile>();
            StorageFolder folder = null;
            var committed = false;
            var original = currentFile;
            var token = activeRenderToken;
            try
            {
                folder = await GetAnnotationFolderAsync();
                var hasSidecars = false;
                foreach (var file in await folder.GetFilesAsync())
                    if (file.Name.StartsWith("page-", StringComparison.Ordinal)) { hasSidecars = true; break; }
                if (!HasUnsavedChanges() && (readingCopy != null || !hasSidecars)) return;
                foreach (var view in pageViews)
                {
                    if (!view.Dirty) continue;
                    embeddedPages.Add(view.Index);
                    foreach (var name in new[] { GetInkFileName(view.Index), HighlightFileName(view.Index) })
                    {
                        var previous = await folder.TryGetItemAsync(name) as StorageFile;
                        backups[name] = previous == null ? null : await previous.CopyAsync(ApplicationData.Current.TemporaryFolder, "uwpdf-backup-" + Guid.NewGuid().ToString("N"));
                    }
                    await SavePageAnnotationsAsync(view, false);
                }
                if (token != activeRenderToken) throw new OperationCanceledException();
                // Include deleted pages so old embedded annotations are removed.
                foreach (var file in await folder.GetFilesAsync())
                {
                    if (!file.Name.StartsWith("page-", StringComparison.Ordinal)) continue;
                    var end = file.Name.IndexOfAny(new[] { '-', '.' }, 5);
                    uint page;
                    if (end >= 0 && uint.TryParse(file.Name.Substring(5, end - 5), out page)) embeddedPages.Add(page);
                }
                if (readingCopy == null)
                {
                    readingCopy = await WritePdfCopyAsync(original, token, false);
                    var cleanDocument = await PdfDocument.LoadFromFileAsync(readingCopy);
                    await renderGate.WaitAsync();
                    try { document = cleanDocument; }
                    finally { renderGate.Release(); }
                    if (textSource != null) await textSource.DisposeAsync();
                    textSource = new PdfTextSource(readingCopy);
                    // Windows.Data.Pdf has no Close/Dispose API. Its previous
                    // wrapper must release the memory-mapped original before
                    // Windows can replace it. Run collection off the UI thread.
                    await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
                }
                staging = await WritePdfCopyAsync(original, token, true);
                // Record ownership before replacement; a failed PDF write keeps
                // its old contents and the dirty flag for retry.
                var state = new JsonObject();
                state["owner"] = JsonValue.CreateStringValue(annotationOwner);
                var pages = new JsonArray();
                foreach (var page in embeddedPages) pages.Add(JsonValue.CreateNumberValue(page));
                state["pages"] = pages;
                var stateFile = await folder.CreateFileAsync("pdf-state.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(stateFile, state.Stringify());
                if (token != activeRenderToken) throw new OperationCanceledException();
                await staging.CopyAndReplaceAsync(original);
                committed = true;
                foreach (var view in pageViews) ClearPageDirty(view);
                UpdateDirtyState();
            }
            finally
            {
                // Restore the last successful editable state if PDF replacement
                // failed, including pages erased during this attempt.
                foreach (var backup in backups)
                {
                    if (!committed)
                    {
                        try
                        {
                            var existing = await folder.TryGetItemAsync(backup.Key) as StorageFile;
                            if (backup.Value == null) { if (existing != null) await existing.DeleteAsync(); }
                            else if (existing != null) await backup.Value.CopyAndReplaceAsync(existing);
                            else await backup.Value.CopyAsync(folder, backup.Key);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Annotation rollback: " + ex.Message); }
                    }
                    await DeleteTemporaryPdfAsync(backup.Value);
                }
                await DeleteTemporaryPdfAsync(staging);
                savingPdf = false;
                MainCommandBar.IsEnabled = true;
                AnnotationToolbar.IsHitTestVisible = true;
                DocumentScroller.IsEnabled = true;
                UpdateDirtyState();
                WakeRenderer();
            }
        }
    }
}
