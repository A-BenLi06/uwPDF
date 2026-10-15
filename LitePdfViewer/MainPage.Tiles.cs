using System;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Data.Xml.Dom;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Notifications;
using Windows.UI.Xaml;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private DispatcherTimer tileTimer;
        private ulong tileDocumentToken;
        private uint tilePage = uint.MaxValue;
        private string tileCover;
        private bool tileBusy;

        // Debounce scrolling; notification work never holds up the reader.
        private void ScheduleReadingTile()
        {
            if (document == null || document.PageCount == 0) return;
            if (tileTimer == null)
            {
                tileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                tileTimer.Tick += async (s, e) => { tileTimer.Stop(); await UpdateReadingTileAsync(); };
            }
            if (tileDocumentToken == activeRenderToken && tilePage == pageIndex) return;
            tileTimer.Stop(); tileTimer.Start();
        }

        private async Task UpdateReadingTileAsync()
        {
            if (tileBusy || document == null || document.PageCount == 0) return;
            tileBusy = true;
            var token = activeRenderToken;
            var pdf = document;
            var index = pageIndex;
            var name = currentFile.Name;
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ReadingTile", CreationCollisionOption.OpenIfExists);
                if (token != activeRenderToken) return;
                if (tileDocumentToken != token || tileCover == null)
                {
                    // Alternate names so Shell does not reuse a cached previous cover.
                    var slot = 1 - Convert.ToInt32(ApplicationData.Current.LocalSettings.Values["TileCoverSlot"] ?? 0);
                    var cover = await folder.CreateFileAsync("cover" + slot + ".png", CreationCollisionOption.ReplaceExisting);
                    using (var page = pdf.GetPage(0))
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        var scale = Math.Min(96 / page.Size.Width, 104 / page.Size.Height);
                        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions {
                            DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
                            DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale) });
                        stream.Seek(0);
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                        var pixels = new byte[134 * 106 * 4];
                        var source = data.DetachPixelData();
                        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
                        for (int y = 0; y < h; y++)
                            Buffer.BlockCopy(source, y * w * 4, pixels, ((y + (106 - h) / 2) * 134 + (134 - w) / 2) * 4, w * 4);
                        await WriteTilePngAsync(cover, 134, 106, pixels);
                    }
                    if (token != activeRenderToken) return;
                    tileCover = "ms-appdata:///local/ReadingTile/" + cover.Name;
                    ApplicationData.Current.LocalSettings.Values["TileCoverSlot"] = slot;
                }
                // Raster progress is supported on the 14393 baseline (adaptive progress is toast-only).
                int percent = (int)Math.Round((index + 1) * 100.0 / pdf.PageCount);
                var barName = "progress" + percent + ".png";
                var bar = await folder.TryGetItemAsync(barName) as StorageFile;
                if (bar == null)
                {
                    bar = await folder.CreateFileAsync(barName, CreationCollisionOption.ReplaceExisting);
                    var pixels = new byte[280 * 4 * 4];
                    for (int p = 0; p < 280 * 4; p++)
                    {
                        byte value = p % 280 < 280 * percent / 100 ? (byte)255 : (byte)130;
                        pixels[p * 4] = pixels[p * 4 + 1] = pixels[p * 4 + 2] = value;
                        pixels[p * 4 + 3] = 255;
                    }
                    await WriteTilePngAsync(bar, 280, 4, pixels);
                }
                if (token != activeRenderToken) return;
                var xml = new XmlDocument();
                var progress = EscapeTileText(L.Format("TileReadingProgress", index + 1, pdf.PageCount));
                var detail = "<text hint-style='base' hint-maxLines='1'>" + EscapeTileText(name) + "</text><text hint-style='captionSubtle'>" + progress + "</text><image src='ms-appdata:///local/ReadingTile/" + barName + "' hint-removeMargin='true'/>";
                xml.LoadXml("<tile><visual><binding template='TileMedium' branding='none' hint-textStacking='center'><image src='" + tileCover + "' hint-align='center' hint-removeMargin='true'/><text hint-style='caption' hint-align='center'>" + EscapeTileText(L.Get("TileRecentReading")) + "</text></binding><binding template='TileWide' branding='name' hint-textStacking='bottom'>" + detail + "</binding><binding template='TileLarge' branding='name' hint-textStacking='center'><image src='" + tileCover + "' hint-align='center'/>" + detail + "</binding></visual></tile>");
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Update(new TileNotification(xml) { ExpirationTime = DateTimeOffset.Now.AddDays(7) });
                tileDocumentToken = token; tilePage = index;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Reading tile: " + ex.Message); }
            finally
            {
                tileBusy = false;
                if (token != activeRenderToken || index != pageIndex) ScheduleReadingTile();
            }
        }

        private static string EscapeTileText(string value)
        {
            return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static async Task WriteTilePngAsync(StorageFile file, uint width, uint height, byte[] pixels)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                stream.Size = 0;
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
                await encoder.FlushAsync();
            }
        }
    }
}

