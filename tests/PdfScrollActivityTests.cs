using System;
using LitePdfViewer;

class PdfScrollActivityTests
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var activity = new PdfScrollActivity();
        Require(activity.IsSettled(now), "New viewport should be settled.");
        activity.Observe(100, 0, true, now);
        Require(!activity.IsSettled(now.AddMilliseconds(119)), "Horizontal panning was treated as idle.");
        Require(activity.IsSettled(now.AddMilliseconds(120)), "Non-gesture pan did not settle.");
        Require(activity.Direction == 1, "Horizontal movement changed page prefetch direction.");
        activity.Observe(100, 500, true, now);
        Require(activity.Direction == 1, "Downward movement direction is wrong.");
        activity.Observe(200, 400, true, now);
        Require(activity.Direction == -1, "Upward movement direction is wrong.");
        activity.Observe(400, 400, true, now.AddMilliseconds(80));
        Require(!activity.IsSettled(now.AddMilliseconds(120)), "Later horizontal input did not extend the busy interval.");
        Require(activity.Direction == -1, "Horizontal movement lost the last vertical direction.");
        activity.BeginGesture();
        activity.Observe(400, 400, false, now);
        Require(!activity.IsSettled(now.AddSeconds(10)), "A paused active gesture allowed refinement.");
        activity.Reset();
        Require(!activity.IsSettled(now), "Document reset incorrectly ended the system gesture.");
        activity.EndGesture();
        Require(activity.IsSettled(now), "Completed gesture did not immediately permit queued refinement.");
        activity.Observe(0.3, 0, true, now);
        activity.Observe(0.6, 0, true, now);
        Require(!activity.IsSettled(now), "Accumulated small movements were lost.");
        activity.Observe(0.6, 0, false, now);
        Require(activity.IsSettled(now), "Final event without movement left the viewport busy.");
        activity.NoteWheel(now);
        activity.Observe(0.6, 0, false, now);
        Require(!activity.IsSettled(now.AddMilliseconds(119)), "Final ViewChanged erased recent wheel activity.");
        activity.EndGesture();
        Require(!activity.IsSettled(now.AddMilliseconds(119)), "Gesture completion erased recent wheel activity.");
        Require(activity.IsSettled(now.AddMilliseconds(120)), "Wheel silence did not permit refinement.");
        Console.WriteLine("PASS: horizontal/vertical activity, direction, active gesture pauses, completion and reset");
        return 0;
    }
}
