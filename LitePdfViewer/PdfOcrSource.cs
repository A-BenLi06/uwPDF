using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LitePdfViewer
{
    // Windows OCR stays offline. A source belongs to one document and language;
    // recognition never shares the viewer's render gate or UI thread.
    internal sealed class PdfOcrSource
    {
        private readonly StorageFile file;
        private readonly string language;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1);
        private readonly Dictionary<uint, List<TextGlyph>> cache = new Dictionary<uint, List<TextGlyph>>();
        private readonly Queue<uint> order = new Queue<uint>();
        public PdfOcrSource(StorageFile file, string language) { this.file = file; this.language = language; }

        internal static OcrEngine CreateEngine(string language)
        {
            if (!string.IsNullOrEmpty(language)) return OcrEngine.TryCreateFromLanguage(new Language(language));
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            var available = OcrEngine.AvailableRecognizerLanguages;
            return engine ?? (available.Count > 0 ? OcrEngine.TryCreateFromLanguage(available[0]) : null);
        }

        public async Task<List<TextGlyph>> ReadAsync(uint index, CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation);
            try
            {
                List<TextGlyph> saved;
                if (cache.TryGetValue(index, out saved)) return saved;
                var glyphs = await Task.Run(async () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var engine = CreateEngine(language);
                    if (engine == null) return new List<TextGlyph>();
                    var pdf = await PdfDocument.LoadFromFileAsync(file);
                    using (var page = pdf.GetPage(index))
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        var scale = Math.Min(2200, OcrEngine.MaxImageDimension) / Math.Max(page.Size.Width, page.Size.Height);
                        var width = Math.Max(1, (uint)Math.Floor(page.Size.Width * scale));
                        var height = Math.Max(1, (uint)Math.Floor(page.Size.Height * scale));
                        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height });
                        cancellation.ThrowIfCancellationRequested();
                        stream.Seek(0);
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        using (var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore))
                        {
                            var result = await engine.RecognizeAsync(bitmap);
                            cancellation.ThrowIfCancellationRequested();
                            return Convert(result, bitmap.PixelWidth, bitmap.PixelHeight);
                        }
                    }
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                while (cache.Count >= 8) cache.Remove(order.Dequeue());
                cache.Add(index, glyphs); order.Enqueue(index);
                return glyphs;
            }
            finally { gate.Release(); }
        }

        private static List<TextGlyph> Convert(OcrResult result, double width, double height)
        {
            var glyphs = new List<TextGlyph>();
            var angle = (result.TextAngle ?? 0) * Math.PI / 180;
            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    var a = Rotate(r.X, r.Y, width, height, angle);
                    var b = Rotate(r.Right, r.Y, width, height, angle);
                    var c = Rotate(r.X, r.Bottom, width, height, angle);
                    var d = Rotate(r.Right, r.Bottom, width, height, angle);
                    var x = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                    var y = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                    var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                    var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                    // OCR exposes word boxes, not character positions. Keeping
                    // whole words avoids invented character geometry.
                    if (glyphs.Count > 0 && glyphs[glyphs.Count - 1].Text != "\n" &&
                        !IsCjk(word.Text) && !IsCjk(glyphs[glyphs.Count - 1].Text))
                        glyphs.Add(new TextGlyph { Text = " " });
                    glyphs.Add(new TextGlyph { Text = word.Text, FromOcr = true, Angle = angle,
                        X = x, Y = y, Width = right - x, Height = bottom - y,
                        TopLeft = a, TopRight = b, BottomLeft = c, BottomRight = d });
                }
                glyphs.Add(new TextGlyph { Text = "\n" });
            }
            return glyphs;
        }
        private static bool IsCjk(string text)
        {
            foreach (var c in text) if ((c >= 0x3000 && c <= 0x9fff) || (c >= 0xf900 && c <= 0xfaff)) return true;
            return false;
        }
        private static Point Rotate(double x, double y, double width, double height, double angle)
        {
            var dx = x - width / 2; var dy = y - height / 2;
            return new Point((width / 2 + dx * Math.Cos(angle) - dy * Math.Sin(angle)) / width,
                (height / 2 + dx * Math.Sin(angle) + dy * Math.Cos(angle)) / height);
        }
    }
}
