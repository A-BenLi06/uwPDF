using System;
using System.Collections.Generic;
using LitePdfViewer;

class PdfRenderSchedulerTests
{
    class Page : IPdfRenderState
    {
        public bool IsRendered { get; set; }
        public bool RenderFailed { get; set; }
        public bool NeedsRefinement;
    }

    static void Expect(IList<Page> pages, int first, int last, bool settled, int expected)
    {
        var refinementChecks = 0;
        var actual = PdfRenderScheduler.FindVisible(pages, first, last, settled,
            p => { refinementChecks++; return p.NeedsRefinement; });
        if (actual != expected) throw new Exception("Expected page " + expected + ", got " + actual);
        if ((!settled || (actual >= 0 && !pages[actual].IsRendered)) && refinementChecks != 0)
            throw new Exception("Refinement ran before missing pages or during a gesture.");
    }

    static int Main()
    {
        var pages = new[] {
            new Page { IsRendered = true, NeedsRefinement = true },
            new Page(), new Page(),
            new Page { IsRendered = true, NeedsRefinement = true }
        };
        // Newly exposed pages must beat an earlier low-resolution surface.
        Expect(pages, 0, 2, true, 1);
        Expect(pages, 0, 2, false, 1);
        pages[1].RenderFailed = true;
        Expect(pages, 0, 2, true, 2);
        pages[2].IsRendered = true;
        Expect(pages, 0, 2, true, 0);
        Expect(pages, 0, 2, false, -1);
        // A viewport jump/reversal only considers its new visible range.
        pages[1].RenderFailed = false;
        Expect(pages, 2, 3, true, 3);
        Expect(pages, 0, 1, true, 1);
        Expect(pages, 1, 1, true, 1);
        Expect(new Page[0], 0, 0, true, -1);
        // Prefetch follows the current direction, stays adjacent, and reuses
        // existing surfaces rather than doing speculative refinements.
        if (PdfRenderScheduler.FindAheadMissing(pages, 0, 0, 1) != 1 ||
            PdfRenderScheduler.FindAheadMissing(pages, 2, 3, -1) != 1 ||
            PdfRenderScheduler.FindAheadMissing(pages, 1, 1, -1) != -1 ||
            PdfRenderScheduler.FindAheadMissing(pages, 0, 0, -1) != -1 ||
            PdfRenderScheduler.FindAheadMissing(pages, 3, 3, 1) != -1)
            throw new Exception("Directional runway selection failed.");
        pages[1].RenderFailed = true;
        if (PdfRenderScheduler.FindAheadMissing(pages, 0, 0, 1) != -1)
            throw new Exception("Failed runway page retried.");
        Console.WriteLine("PASS: visible blank priority, gesture deferral, failures, viewport changes and bounded directional runway");
        return 0;
    }
}
