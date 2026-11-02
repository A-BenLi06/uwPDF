using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private static void DraftAssert(bool condition, string message, StringBuilder report)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.AppendLine("PASS: " + message);
        }

        private InkStroke DraftTestStroke(double y)
        {
            var builder = new InkStrokeBuilder();
            builder.SetDefaultDrawingAttributes(CreateInkAttributes());
            return builder.CreateStroke(new[] { new Point(80, y), new Point(180, y + 30) });
        }

        private async Task DraftTestInkAsync()
        {
            var view = pageViews[0];
            await LoadInkAsync(view);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (view.IsLoadingInk && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (!view.InkLoaded || view.IsLoadingInk) throw new InvalidOperationException("Ink did not finish loading.");
        }

        private async Task DraftTestReloadWithoutMemoryAsync(StorageFile file)
        {
            // Model process memory being gone; do not persist it during reload.
            await PauseDraftWritesAsync();
            draftReady = false; draftRequested = false;
            foreach (var view in pageViews) view.Dirty = false;
            dirtyCount = 0;
            await LoadDocumentAsync(file);
            await DraftTestInkAsync();
        }

        public async void RunDraftRecoveryQa(bool prepare)
        {
            var output = await ApplicationData.Current.LocalFolder.GetFolderAsync("draft-recovery-qa");
            var report = new StringBuilder();
            try
            {
                var file = await output.GetFileAsync((await FileIO.ReadTextAsync(await output.GetFileAsync("fixture-name.txt"))).Trim());
                if (prepare)
                {
                    var legacyName = "ink-" + StableId(file.Path + file.DateCreated.UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var legacy = await ApplicationData.Current.LocalFolder.CreateFolderAsync(legacyName);
                    await legacy.CreateFileAsync("migration-sentinel.txt");
                }
                await LoadDocumentAsync(file);
                if (document == null) throw new InvalidOperationException("Fixture did not open.");
                await DraftTestInkAsync();
                var view = pageViews[0];
                var annotations = await GetAnnotationFolderAsync();
                if (prepare)
                {
                    DraftAssert(annotations.Name.StartsWith("ink-v2-", StringComparison.Ordinal) &&
                        await annotations.TryGetItemAsync("migration-sentinel.txt") != null, "Legacy editable storage migrates to the stable document key", report);
                    var original = await FileIO.ReadBufferAsync(file);
                    view.EnsureStrokes().AddStroke(DraftTestStroke(100));
                    var highlight = new TextHighlight { Color = Colors.Yellow };
                    highlight.Rectangles.Add(new Rect(.1, .2, .3, .04));
                    view.Highlights.Add(highlight); view.HighlightsLoaded = true;
                    MarkPageDirty(view);
                    // The first write is awaiting storage while live ink changes.
                    view.Strokes.AddStroke(DraftTestStroke(200));
                    MarkPageDirty(view);
                    await FlushDraftAsync();
                    DraftAssert(draftError == null && HasUnsavedChanges(), "Draft persistence keeps PDF changes pending", report);
                    DraftAssert(CryptographicBuffer.Compare(original, await FileIO.ReadBufferAsync(file)), "Draft does not modify the original PDF", report);
                    var root = await annotations.GetFolderAsync("drafts");
                    var latest = NewestDrafts(await root.GetFoldersAsync())[0];
                    var ink = new InkStrokeContainer();
                    using (var input = await (await latest.GetFileAsync(GetInkFileName(0))).OpenSequentialReadAsync()) await ink.LoadAsync(input);
                    DraftAssert(ink.GetStrokes().Count == 2, "Edits arriving during I/O reach the latest immutable generation", report);
                    var interrupted = await root.CreateFolderAsync("pending-interrupted-test");
                    await FileIO.WriteTextAsync(await interrupted.CreateFileAsync("draft.json"), "incomplete");
                    report.AppendLine("READY: terminate this test process and reopen in a fresh process.");
                }
                else
                {
                    DraftAssert(view.Strokes.GetStrokes().Count == 2 && view.Highlights.Count == 1 && view.Dirty,
                        "Fresh process restores ink and highlights as unsaved edits", report);
                    DraftAssert(draftRecoveryNotice == "DraftRestored", "Incomplete pending generation is ignored", report);
                    await SaveCurrentAnnotationsAsync();
                    DraftAssert(!HasUnsavedChanges(), "Explicit Save commits recovered annotations", report);
                    await LoadDocumentAsync(file); await DraftTestInkAsync(); view = pageViews[0];
                    report.AppendLine("Reopen ink=" + (view.Strokes == null ? -1 : view.Strokes.GetStrokes().Count) +
                        ", highlights=" + view.Highlights.Count + ", loaded=" + view.InkLoaded + ", dirty=" + dirtyCount);
                    DraftAssert(!HasUnsavedChanges() && view.Strokes.GetStrokes().Count == 2, "Committed draft does not replay on reopen", report);

                    view.Strokes.Clear(); view.Highlights.Clear(); MarkPageDirty(view);
                    await FlushDraftAsync();
                    await DraftTestReloadWithoutMemoryAsync(file); view = pageViews[0];
                    DraftAssert(view.Dirty && view.Strokes.GetStrokes().Count == 0 && view.Highlights.Count == 0,
                        "Empty ink and highlights recover as deletions, not old saved marks", report);
                    await SaveCurrentAnnotationsAsync();
                    await LoadDocumentAsync(file); await DraftTestInkAsync(); view = pageViews[0];
                    DraftAssert(!view.Dirty && view.Strokes.GetStrokes().Count == 0 && view.Highlights.Count == 0,
                        "Saving recovered deletions removes saved editable annotations", report);

                    view.Strokes.AddStroke(DraftTestStroke(100)); MarkPageDirty(view); await FlushDraftAsync();
                    view.Strokes.AddStroke(DraftTestStroke(200)); MarkPageDirty(view); await FlushDraftAsync();
                    await PauseDraftWritesAsync();
                    var root = await annotations.GetFolderAsync("drafts");
                    var latest = NewestDrafts(await root.GetFoldersAsync())[0];
                    await (await latest.GetFileAsync(GetInkFileName(0))).DeleteAsync();
                    await DraftTestReloadWithoutMemoryAsync(file); view = pageViews[0];
                    DraftAssert(view.Strokes.GetStrokes().Count == 1 && draftRecoveryNotice == "DraftPartialRecovery",
                        "Damaged newest generation falls back without partially applying its pages", report);
                    await FlushDraftAsync();

                    await PauseDraftWritesAsync();
                    root = await annotations.GetFolderAsync("drafts");
                    await root.RenameAsync("drafts-test-backup");
                    var blocker = await annotations.CreateFileAsync("drafts");
                    ResumeDraftWrites();
                    view.Strokes.AddStroke(DraftTestStroke(220)); MarkPageDirty(view); await FlushDraftAsync();
                    DraftAssert(draftError != null && view.Dirty && DraftStatusText.Text == L.Get("DraftFailed"),
                        "Storage failure is visible and does not clear unsaved edits", report);
                    await blocker.DeleteAsync(); await root.RenameAsync("drafts"); await FlushDraftAsync();
                    DraftAssert(draftError == null, "A later flush retries a failed draft write", report);

                    var failed = false;
                    using (var locked = new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                    {
                        try { await SaveCurrentAnnotationsAsync(); }
                        catch { failed = true; }
                    }
                    DraftAssert(failed && HasUnsavedChanges(), "Failed PDF save preserves dirty edits and draft", report);
                    await FlushDraftAsync();

                    await DiscardDraftAsync();
                    foreach (var page in pageViews) page.Dirty = false;
                    dirtyCount = 0; ResumeDraftWrites();
                    await LoadDocumentAsync(file); await DraftTestInkAsync(); view = pageViews[0];
                    DraftAssert(!HasUnsavedChanges() && view.Strokes.GetStrokes().Count == 0,
                        "Explicit discard prevents unsaved edits from reappearing", report);

                    view.Strokes.AddStroke(DraftTestStroke(110)); MarkPageDirty(view); await FlushDraftAsync();
                    await PauseDraftWritesAsync();
                    root = await annotations.GetFolderAsync("drafts");
                    foreach (var generation in NewestDrafts(await root.GetFoldersAsync()))
                    {
                        var metaFile = await generation.GetFileAsync("draft.json");
                        var meta = JsonObject.Parse(await FileIO.ReadTextAsync(metaFile));
                        meta["source"] = JsonValue.CreateStringValue("different PDF version");
                        await FileIO.WriteTextAsync(metaFile, meta.Stringify());
                    }
                    await DraftTestReloadWithoutMemoryAsync(file);
                    DraftAssert(!HasUnsavedChanges() && pageViews[0].Strokes.GetStrokes().Count == 0 &&
                        NewestDrafts(await root.GetFoldersAsync()).Count > 0 && DraftStatusText.Text == L.Get("DraftConflict"),
                        "Source mismatch leaves the PDF clean and retains conflicting drafts", report);
                    var sourceBeforeExternalChange = await DraftSourceAsync(file);
                    await PauseDraftWritesAsync(); draftReady = false;
                    ResetSearch(); ++activeRenderToken; document = null; ResetPageViews();
                    await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
                    await FileIO.AppendTextAsync(file, "\n% externally changed by draft QA\n");
                    await LoadDocumentAsync(file); await DraftTestInkAsync();
                    var archived = false;
                    foreach (var candidate in await ApplicationData.Current.LocalFolder.GetFoldersAsync())
                    {
                        if (!candidate.Name.StartsWith("ink-archived-", StringComparison.Ordinal)) continue;
                        var marker = await candidate.TryGetItemAsync("document-source.txt") as StorageFile;
                        if (marker != null && await FileIO.ReadTextAsync(marker) == sourceBeforeExternalChange) archived = true;
                    }
                    DraftAssert(annotationSourceChanged && archived &&
                        !HasUnsavedChanges() && pageViews[0].Strokes.GetStrokes().Count == 0,
                        "Externally modified PDF archives old editable data without overlaying it", report);
                    report.AppendLine("PASS: all draft recovery QA completed.");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("FAIL: " + ex);
                report.AppendLine("Draft error: " + draftError);
                report.AppendLine("Draft state: ready=" + draftReady + ", paused=" + draftPaused + ", dirty=" + dirtyCount);
            }
            await FileIO.WriteTextAsync(await output.CreateFileAsync(prepare ? "write-report.txt" : "read-report.txt", CreationCollisionOption.ReplaceExisting), report.ToString());
        }
    }
}
