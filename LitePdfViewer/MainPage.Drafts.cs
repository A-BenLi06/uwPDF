using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private Task draftWriteTask;
        private bool draftRequested, draftPaused, draftReady;
        private string draftSource;
        private string draftRecoveryNotice;
        private bool annotationSourceChanged;
        private Exception draftError;
        private long draftRevision, persistedDraftRevision;

        private sealed class DraftPage
        {
            public uint Index;
            public InkStrokeContainer Ink;
            public string Highlights;
        }

        private static async Task<string> DraftSourceAsync(StorageFile file)
        {
            var info = await file.GetBasicPropertiesAsync();
            return file.Path + "|" + info.Size.ToString(CultureInfo.InvariantCulture) + "|" + info.DateModified.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static async Task WriteAnnotationSourceAsync(StorageFolder folder, StorageFile file)
        {
            await FileIO.WriteTextAsync(await folder.CreateFileAsync("document-source.txt", CreationCollisionOption.ReplaceExisting), await DraftSourceAsync(file));
        }

        private void SetDraftStatus(string key)
        {
            if (DraftStatusText == null) return;
            DraftStatusText.Text = key == null ? "" : L.Get(key);
            DraftStatusText.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(DraftStatusText, DraftStatusText.Text);
            AutomationProperties.SetName(DraftStatusText, DraftStatusText.Text);
        }

        private void QueueDraftSave()
        {
            if (!draftReady || draftPaused || !HasUnsavedChanges()) return;
            if (draftRevision == persistedDraftRevision && draftError == null) return;
            draftRequested = true;
            if (draftWriteTask == null || draftWriteTask.IsCompleted) draftWriteTask = WriteDraftLoopAsync();
        }

        // Capture on the UI thread before the first await. Disk I/O never reads
        // live containers that a new stroke, erase or lasso transform can mutate.
        private List<DraftPage> CaptureDraftPages()
        {
            var result = new List<DraftPage>();
            foreach (var view in pageViews)
            {
                if (!view.Dirty) continue;
                if (view.IsLoadingInk) throw new InvalidOperationException("Annotation loading has not completed.");
                var page = new DraftPage { Index = view.Index, Highlights = view.HighlightsLoaded ? SerializeHighlights(view) : null };
                if (view.InkLoaded)
                {
                    page.Ink = new InkStrokeContainer();
                    if (view.Strokes != null)
                        foreach (var stroke in view.Strokes.GetStrokes()) page.Ink.AddStroke(stroke.Clone());
                }
                result.Add(page);
            }
            return result;
        }

        private async Task WriteDraftLoopAsync()
        {
            while (draftRequested && !draftPaused && draftReady && HasUnsavedChanges())
            {
                draftRequested = false;
                var token = activeRenderToken;
                var revision = draftRevision;
                var source = draftSource;
                StorageFolder pending = null;
                try
                {
                    SetDraftStatus("DraftSaving");
                    var pages = CaptureDraftPages();
                    var folder = await GetAnnotationFolderAsync();
                    var root = await folder.CreateFolderAsync("drafts", CreationCollisionOption.OpenIfExists);
                    pending = await root.CreateFolderAsync("pending-" + Guid.NewGuid().ToString("N"));
                    var entries = new JsonArray();
                    foreach (var page in pages)
                    {
                        var hasInk = page.Ink != null && page.Ink.GetStrokes().Count > 0;
                        if (hasInk)
                        {
                            var file = await pending.CreateFileAsync(GetInkFileName(page.Index));
                            using (var output = await file.OpenAsync(FileAccessMode.ReadWrite)) await page.Ink.SaveAsync(output);
                        }
                        if (page.Highlights != null)
                            await FileIO.WriteTextAsync(await pending.CreateFileAsync(HighlightFileName(page.Index)), page.Highlights);
                        entries.Add(new JsonObject {
                            ["page"] = JsonValue.CreateNumberValue(page.Index),
                            ["inkLoaded"] = JsonValue.CreateBooleanValue(page.Ink != null),
                            ["inkFile"] = JsonValue.CreateBooleanValue(hasInk),
                            ["highlightsLoaded"] = JsonValue.CreateBooleanValue(page.Highlights != null) });
                    }
                    var metadata = new JsonObject {
                        ["format"] = JsonValue.CreateNumberValue(1), ["source"] = JsonValue.CreateStringValue(source),
                        ["pages"] = entries };
                    await FileIO.WriteTextAsync(await pending.CreateFileAsync("draft.json"), metadata.Stringify());
                    if (token != activeRenderToken) return;
                    // Only the rename publishes a complete generation. A crash
                    // during any file write leaves the previous draft intact.
                    await pending.RenameAsync("draft-" + DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
                    pending = null;
                    draftError = null;
                    persistedDraftRevision = revision;
                    draftRequested = draftRevision != revision;
                    if (!draftRequested) SetDraftStatus(draftRecoveryNotice ?? "DraftSaved");
                    await PruneDraftsAsync(root, source);
                }
                catch (Exception ex)
                {
                    draftError = ex;
                    if (token == activeRenderToken) SetDraftStatus("DraftFailed");
                    System.Diagnostics.Debug.WriteLine("Draft write: " + ex.Message);
                    // Retry on the next edit or explicit flush, not in a failure loop.
                    break;
                }
                finally
                {
                    if (pending != null)
                    {
                        try { await pending.DeleteAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Draft cleanup: " + ex.Message); }
                    }
                }
            }
        }

        public async Task FlushDraftAsync()
        {
            QueueDraftSave();
            var operation = draftWriteTask;
            if (operation != null) await operation;
        }

        private async Task PauseDraftWritesAsync()
        {
            draftPaused = true;
            if (draftWriteTask != null) await draftWriteTask;
        }

        private void ResumeDraftWrites()
        {
            draftPaused = false;
            if (HasUnsavedChanges()) QueueDraftSave();
        }

        private static List<StorageFolder> NewestDrafts(IReadOnlyList<StorageFolder> folders)
        {
            var result = new List<StorageFolder>();
            foreach (var folder in folders) if (folder.Name.StartsWith("draft-", StringComparison.Ordinal)) result.Add(folder);
            result.Sort((a, b) => StringComparer.Ordinal.Compare(b.Name, a.Name));
            return result;
        }

        private static async Task PruneDraftsAsync(StorageFolder root, string source)
        {
            var retained = 0;
            foreach (var folder in NewestDrafts(await root.GetFoldersAsync()))
            {
                try
                {
                    var metadata = JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("draft.json")));
                    // Keep drafts from externally modified PDFs for manual recovery.
                    if (metadata.GetNamedString("source") != source) continue;
                    if (++retained > 2) await folder.DeleteAsync();
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Draft retention: " + ex.Message); }
            }
        }

        private async Task RecoverDraftAsync(ulong token)
        {
            var file = currentFile;
            var source = await DraftSourceAsync(file);
            var annotations = await GetAnnotationFolderAsync();
            var root = await annotations.TryGetItemAsync("drafts") as StorageFolder;
            if (token != activeRenderToken) return;
            draftSource = source;
            var warning = false;
            if (root != null)
            {
                foreach (var folder in NewestDrafts(await root.GetFoldersAsync()))
                {
                    try
                    {
                        var metadata = JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("draft.json")));
                        if (token != activeRenderToken) return;
                        if (metadata.GetNamedNumber("format") != 1 || metadata.GetNamedString("source") != source)
                        { warning = true; continue; }
                        var recovered = new List<DraftPage>();
                        var seen = new HashSet<uint>();
                        foreach (var value in metadata.GetNamedArray("pages"))
                        {
                            var entry = value.GetObject();
                            var number = entry.GetNamedNumber("page");
                            if (number < 0 || number >= pageViews.Count || number != Math.Floor(number) || !seen.Add((uint)number))
                                throw new InvalidOperationException("Invalid draft page.");
                            var page = new DraftPage { Index = (uint)number };
                            if (entry.GetNamedBoolean("inkLoaded"))
                            {
                                page.Ink = new InkStrokeContainer();
                                if (entry.GetNamedBoolean("inkFile"))
                                    using (var input = await (await folder.GetFileAsync(GetInkFileName(page.Index))).OpenSequentialReadAsync())
                                        await page.Ink.LoadAsync(input);
                            }
                            if (entry.GetNamedBoolean("highlightsLoaded"))
                            {
                                page.Highlights = await FileIO.ReadTextAsync(await folder.GetFileAsync(HighlightFileName(page.Index)));
                                JsonArray.Parse(page.Highlights); // Validate before changing any live page.
                            }
                            recovered.Add(page);
                        }
                        if (recovered.Count == 0) throw new InvalidOperationException("Empty draft.");
                        if (token != activeRenderToken) return;
                        // Validate every highlight through the real parser into
                        // temporary views before applying anything to the document.
                        var views = new List<PageView>();
                        foreach (var page in recovered)
                        {
                            var view = new PageView { Index = page.Index, Strokes = page.Ink, InkLoaded = page.Ink != null };
                            if (page.Highlights != null) await LoadTextHighlightsAsync(view, folder, token);
                            views.Add(view);
                        }
                        if (token != activeRenderToken) return;
                        foreach (var restored in views)
                        {
                            var view = pageViews[(int)restored.Index];
                            view.Strokes = restored.Strokes; view.InkLoaded = restored.InkLoaded;
                            view.Highlights.AddRange(restored.Highlights); view.HighlightsLoaded = restored.HighlightsLoaded;
                            view.Dirty = true; view.Revision++; dirtyCount++;
                        }
                        draftReady = true;
                        draftRevision = persistedDraftRevision = 1;
                        draftRecoveryNotice = warning ? "DraftPartialRecovery" : "DraftRestored";
                        SetDraftStatus(draftRecoveryNotice);
                        return;
                    }
                    catch (Exception ex)
                    {
                        warning = true;
                        System.Diagnostics.Debug.WriteLine("Draft recovery: " + ex.Message);
                    }
                }
            }
            if (token != activeRenderToken) return;
            draftReady = true;
            SetDraftStatus(annotationSourceChanged ? "AnnotationSourceChanged" : warning ? "DraftConflict" : null);
        }

        private async Task DiscardDraftAsync()
        {
            await PauseDraftWritesAsync();
            try { await DeleteCurrentDraftsAsync(); }
            catch { ResumeDraftWrites(); throw; }
            draftRequested = false; draftError = null; draftRecoveryNotice = null;
            persistedDraftRevision = draftRevision;
            SetDraftStatus(null);
        }

        private async Task DeleteCurrentDraftsAsync()
        {
            var folder = await GetAnnotationFolderAsync();
            var root = await folder.TryGetItemAsync("drafts") as StorageFolder;
            if (root == null) return;
            foreach (var generation in NewestDrafts(await root.GetFoldersAsync()))
            {
                JsonObject metadata;
                try { metadata = JsonObject.Parse(await FileIO.ReadTextAsync(await generation.GetFileAsync("draft.json"))); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Unreadable draft retained: " + ex.Message); continue; }
                if (metadata.GetNamedString("source") == draftSource) await generation.DeleteAsync();
            }
        }

        private async Task FinishDraftSaveAsync()
        {
            // Save has disabled editing and paused the draft writer. Invalidate
            // generations before re-enabling input so saved drafts cannot replay.
            try { await DeleteCurrentDraftsAsync(); }
            finally
            {
                draftSource = await DraftSourceAsync(currentFile);
                await WriteAnnotationSourceAsync(await GetAnnotationFolderAsync(), currentFile);
                draftRequested = false; draftError = null; draftRecoveryNotice = null;
                persistedDraftRevision = draftRevision;
                SetDraftStatus(null);
            }
        }
    }
}
