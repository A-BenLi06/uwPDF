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
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private bool exportBusy;

        private static JsonArray Numbers(params double[] values)
        {
            var array = new JsonArray();
            foreach (var n in values) array.Add(JsonValue.CreateNumberValue(n));
            return array;
        }
        private static JsonObject ExportItem(uint page, string kind, Color color, double opacity)
        {
            var item = new JsonObject();
            item["page"] = JsonValue.CreateNumberValue(page);
            item["kind"] = JsonValue.CreateStringValue(kind);
            item["color"] = Numbers(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            item["opacity"] = JsonValue.CreateNumberValue(opacity * color.A / 255.0);
            return item;
        }

        private async Task AppendExportAnnotationsAsync(PdfAnnotationWriter writer, ulong token)
        {
            // Save dirty pages first, then enumerate only actual annotation files.
            // Export does not load every page into the viewer's caches.
            if (token != activeRenderToken) throw new OperationCanceledException();
            var layoutWidth = ComputeColumnWidth();
            await SaveCurrentAnnotationsAsync();
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

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (document == null || currentFile == null || exportBusy) return;
            exportBusy = true;
            ExportButton.IsEnabled = false;
            StorageFile staging = null;
            var original = currentFile;
            var token = activeRenderToken;
            var completed = false;
            try
            {
                ToolTipService.SetToolTip(ExportButton, "正在生成带批注的 PDF…");
                using (var input = await original.OpenReadAsync())
                {
                    var writer = await PdfAnnotationWriter.OpenAsync(input);
                    try
                    {
                        await AppendExportAnnotationsAsync(writer, token);
                        staging = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("uwpdf-export-" + Guid.NewGuid().ToString("N") + ".pdf");
                        using (var output = await staging.OpenAsync(FileAccessMode.ReadWrite)) await writer.WriteAsync(output);
                    }
                    finally
                    {
                        // Closing the last native lease releases the PDF object/font
                        // stores. Await worker cleanup before closing the input.
                        await Task.Run(() => writer.Dispose());
                    }
                }
                if (token != activeRenderToken) return;
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = original.DisplayName + "-annotated" };
                picker.FileTypeChoices.Add("PDF", new[] { ".pdf" });
                var target = await picker.PickSaveFileAsync();
                if (target == null) return;
                if (target.Path.Equals(original.Path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请选择另一个文件名，保留原始 PDF。");
                await staging.CopyAndReplaceAsync(target);
                completed = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await new ContentDialog { Title = "导出失败", Content = ex.Message, PrimaryButtonText = "确定" }.ShowAsync();
            }
            finally
            {
                if (staging != null)
                {
                    try { await staging.DeleteAsync(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Export temporary-file cleanup: " + ex.Message); }
                }
                exportBusy = false;
                ExportButton.IsEnabled = document != null;
                ToolTipService.SetToolTip(ExportButton, completed ? "已导出批注副本" : "导出带批注的 PDF 副本");
            }
        }
    }
}
