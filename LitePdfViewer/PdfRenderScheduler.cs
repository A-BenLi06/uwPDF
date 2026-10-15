using System;
using System.Collections.Generic;

namespace LitePdfViewer
{
    internal interface IPdfRenderState
    {
        bool IsRendered { get; }
        bool RenderFailed { get; }
    }

    internal static class PdfRenderScheduler
    {
        // Fill the entire viewport before improving any existing surface. A
        // first-page refinement must never delay a newly exposed second page.
        public static int FindVisible<T>(IList<T> pages, int first, int last,
            bool allowRefinement, Func<T, bool> needsRefinement) where T : IPdfRenderState
        {
            first = Math.Max(0, first);
            last = Math.Min(pages.Count - 1, last);
            for (var i = first; i <= last; i++)
                if (!pages[i].RenderFailed && !pages[i].IsRendered) return i;

            if (allowRefinement)
                for (var i = first; i <= last; i++)
                    if (!pages[i].RenderFailed && needsRefinement(pages[i])) return i;
            return -1;
        }

        // One-page runway during a gesture, after every visible page has a
        // surface. Do not enqueue distant pages or refine an existing runway.
        public static int FindAheadMissing<T>(IList<T> pages, int first, int last,
            int direction) where T : IPdfRenderState
        {
            var index = direction < 0 ? first - 1 : last + 1;
            if (index < 0 || index >= pages.Count) return -1;
            return !pages[index].RenderFailed && !pages[index].IsRendered ? index : -1;
        }
    }
}
