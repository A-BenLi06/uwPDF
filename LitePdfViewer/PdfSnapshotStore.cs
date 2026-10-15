using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace LitePdfViewer
{
    internal sealed class PdfSnapshot
    {
        public StorageFolder Folder;
        public DateTimeOffset Created;
        public string Kind;
        public uint Page;
        public override string ToString() { return Created.ToLocalTime().ToString("G", CultureInfo.CurrentCulture) + "\n" + L.Get(Kind == "baseline" ? "HistoryBaseline" : Kind == "rescue" ? "HistoryRescue" : "HistorySaved"); }
    }
    internal static class PdfSnapshotStore
    {
        public static bool IsEditableFile(string name)
        {
            return name == "pdf-state.json" || (name.StartsWith("page-", StringComparison.Ordinal) &&
                (name.EndsWith(".isf", StringComparison.Ordinal) || name.EndsWith("-highlights.json", StringComparison.Ordinal)));
        }
        private static Task<StorageFolder> Root(StorageFolder annotations)
        { return annotations.CreateFolderAsync("history", CreationCollisionOption.OpenIfExists).AsTask(); }

        public static async Task<List<PdfSnapshot>> ListAsync(StorageFolder annotations)
        {
            var result = new List<PdfSnapshot>();
            foreach (var folder in await (await Root(annotations)).GetFoldersAsync())
            {
                if (!folder.Name.StartsWith("snapshot-", StringComparison.Ordinal)) continue;
                try
                {
                    var json = JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("snapshot.json")));
                    result.Add(new PdfSnapshot { Folder = folder, Created = DateTimeOffset.Parse(json.GetNamedString("created"), CultureInfo.InvariantCulture),
                        Kind = json.GetNamedString("kind"), Page = (uint)json.GetNamedNumber("page", 0) });
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Snapshot metadata: " + ex.Message); }
            }
            result.Sort((a, b) => b.Created.CompareTo(a.Created));
            return result;
        }
        // Incomplete snapshots remain invisible. PDF replacement and snapshot
        // publication are separate commits; only complete copies enter history.
        public static async Task<PdfSnapshot> PrepareAsync(StorageFile pdf, StorageFolder annotations, string kind, uint page)
        {
            var root = await Root(annotations);
            var folder = await root.CreateFolderAsync("pending-" + Guid.NewGuid().ToString("N"));
            try
            {
                await pdf.CopyAsync(folder, "document.pdf");
                var data = await folder.CreateFolderAsync("annotations");
                foreach (var file in await annotations.GetFilesAsync()) if (IsEditableFile(file.Name)) await file.CopyAsync(data, file.Name);
                var created = DateTimeOffset.UtcNow;
                var metadata = new JsonObject {
                    ["created"] = JsonValue.CreateStringValue(created.ToString("o", CultureInfo.InvariantCulture)),
                    ["kind"] = JsonValue.CreateStringValue(kind), ["page"] = JsonValue.CreateNumberValue(page) };
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("snapshot.json"), metadata.Stringify());
                return new PdfSnapshot { Folder = folder, Created = created, Kind = kind, Page = page };
            }
            catch { await folder.DeleteAsync(); throw; }
        }
        public static async Task PublishAsync(PdfSnapshot snapshot)
        {
            await snapshot.Folder.RenameAsync("snapshot-" + snapshot.Created.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        }
        public static async Task ApplyEditableAsync(StorageFolder from, StorageFolder to)
        {
            foreach (var file in await to.GetFilesAsync()) if (IsEditableFile(file.Name)) await file.DeleteAsync();
            foreach (var file in await from.GetFilesAsync()) if (IsEditableFile(file.Name)) await file.CopyAsync(to, file.Name);
        }
        public static async Task PruneAsync(StorageFolder annotations)
        {
            var entries = await ListAsync(annotations);
            // Retain the baseline plus the most recent 19 saves. A large PDF
            // always retains two versions; further copies have a 512 MiB budget.
            ulong bytes = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var info = await (await entries[i].Folder.GetFileAsync("document.pdf")).GetBasicPropertiesAsync();
                bytes += info.Size;
                if (i >= 2 && entries[i].Kind != "baseline" && (i >= 19 || bytes > 512UL * 1024 * 1024))
                    await entries[i].Folder.DeleteAsync();
            }
        }
    }
}
