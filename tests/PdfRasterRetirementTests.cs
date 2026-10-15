using System;
using LitePdfViewer;

class PdfRasterRetirementTests
{
    sealed class Resource : IDisposable
    {
        public int Releases;
        public void Dispose() { Releases++; }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var queue = new PdfRasterRetirement(100, 4);
        var first = new Resource(); var second = new Resource();
        queue.Retire(first, 40);
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(10));
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(10));
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(9));
        Require(first.Releases == 0 && queue.Bytes == 40, "A duplicate/old frame prematurely released the old image.");
        queue.Retire(second, 30);
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(20));
        Require(first.Releases == 1 && second.Releases == 0 && queue.Bytes == 30, "A replacement did not receive its own two-frame grace period.");
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(30));
        Require(second.Releases == 1 && queue.Bytes == 0 && !queue.HasPending, "Completed frames retained raster memory.");

        first = new Resource(); second = new Resource();
        queue.Retire(first, 70); queue.Retire(second, 60);
        Require(first.Releases == 1 && second.Releases == 0 && queue.Bytes == 60, "Byte pressure failed to evict the oldest raster.");
        var oversized = new Resource(); queue.Retire(oversized, 101);
        Require(oversized.Releases == 1 && queue.Bytes == 60, "An oversized retirement exceeded the cap or evicted an unrelated item.");
        queue.Clear(); queue.Clear();
        Require(second.Releases == 1 && queue.Bytes == 0, "Hide/suspend/reset cleanup leaked or double-disposed resources.");

        queue = new PdfRasterRetirement(100, 2);
        first = new Resource(); second = new Resource(); var third = new Resource();
        queue.Retire(first, 0); queue.Retire(second, 0); queue.Retire(third, 0);
        Require(first.Releases == 1 && second.Releases == 0 && third.Releases == 0, "Small rasters bypassed the entry cap.");
        queue.Clear();
        Require(second.Releases == 1 && third.Releases == 1, "Cleanup failed after entry pressure.");
        queue = new PdfRasterRetirement(100, 4);
        first = new Resource(); second = new Resource(); third = new Resource();
        queue.Retire(first, 40); queue.Retire(second, 30);
        queue.SetByteLimit(35);
        Require(first.Releases == 1 && second.Releases == 0 && queue.Bytes == 30, "A smaller system budget did not release older rasters immediately.");
        queue.SetByteLimit(0);
        queue.Retire(third, 1);
        Require(second.Releases == 1 && third.Releases == 1 && !queue.HasPending, "Critical pressure retained a presentation queue.");
        var empty = new Resource(); queue.Retire(empty, 0);
        Require(empty.Releases == 1 && !queue.HasPending, "Critical pressure retained a zero-byte resource.");
        queue.SetByteLimit(100);
        first = new Resource(); queue.Retire(first, 40);
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(10));
        Require(first.Releases == 0, "Budget recovery bypassed the frame grace period.");
        queue.AdvanceFrame(TimeSpan.FromMilliseconds(20));
        Require(first.Releases == 1 && queue.Bytes == 0, "Budget recovery leaked a queued raster.");
        Console.WriteLine("PASS: production raster retirement frame lifetime, byte/entry caps and shutdown cleanup");
    }
}
