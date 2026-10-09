using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PdfNative;
#if CPPWINRT_RENDERER
using PdfTextDocument = PdfNative.Rendering.PdfTextDocument;
#endif
using Windows.Foundation;
using Windows.Storage;

namespace LitePdfViewer
{
    internal sealed class TextGlyph
    {
        public string Text;
        // Fractions of the rotated CropBox; no canvas font measurements.
        public double X, Y, Width, Height, Angle;
        public Point TopLeft, TopRight, BottomLeft, BottomRight;
    }

    // A native auxiliary engine reads a seekable file stream on a worker.
    // Windows.Data.Pdf remains the only page renderer.
    internal sealed class PdfTextSource : IDisposable
    {
        private readonly StorageFile file;
        private PdfTextDocument document;
        private Task initialization;
        private bool disposed;
        private readonly SemaphoreSlim textGate = new SemaphoreSlim(1, 1);

        public PdfTextSource(StorageFile file) { this.file = file; }
        public bool IsUnavailable { get { return initialization != null && initialization.IsFaulted; } }

        private async Task InitializeAsync()
        {
            using (var stream = await file.OpenReadAsync())
            {
                if (disposed) throw new ObjectDisposedException("PdfTextSource");
                var opened = await PdfTextDocument.OpenAsync(stream);
                if (disposed)
                {
                    opened.Dispose();
                    throw new ObjectDisposedException("PdfTextSource");
                }
                document = opened;
            }
        }

        public async Task<List<TextGlyph>> ReadPageAsync(uint index, CancellationToken cancellation = default(CancellationToken))
        {
            await textGate.WaitAsync(cancellation);
            try
            {
                if (disposed) throw new ObjectDisposedException("PdfTextSource");
                if (initialization == null) initialization = InitializeAsync();
                await initialization;
                cancellation.ThrowIfCancellationRequested();
                if (disposed) throw new ObjectDisposedException("PdfTextSource");
                var timer = Stopwatch.StartNew();
                var page = await document.ReadPageAsync(index);
                cancellation.ThrowIfCancellationRequested();
                if (disposed) throw new ObjectDisposedException("PdfTextSource");
                var text = page.Text;
                var values = page.Coordinates;
                var glyphs = new List<TextGlyph>(text.Length);
                for (var i = 0; i < text.Length; i++)
                {
                    var p = i * 9;
                    var a = new Point(values[p], values[p + 1]);
                    var b = new Point(values[p + 2], values[p + 3]);
                    var c = new Point(values[p + 4], values[p + 5]);
                    var d = new Point(values[p + 6], values[p + 7]);
                    var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                    var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                    var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                    var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                    var units = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                    glyphs.Add(new TextGlyph { Text = text.Substring(i, units), X = left, Y = top,
                        Width = right - left, Height = bottom - top, Angle = values[p + 8],
                        TopLeft = a, TopRight = b, BottomLeft = c, BottomRight = d });
                    i += units - 1;
                }
                Debug.WriteLine("PDF text: page " + (index + 1) + " " + glyphs.Count + " glyphs " + timer.ElapsedMilliseconds + "ms");
                return glyphs;
            }
            finally { textGate.Release(); }
        }

        public void Dispose()
        {
            disposed = true;
            var released = document;
            document = null;
            if (released != null)
            {
                // Releasing font/object stores must not delay the next file's UI.
                var ignored = Task.Run(() => released.Dispose());
            }
        }
    }
}
