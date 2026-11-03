using System.Text.Json;
namespace Atlas.Core;

public sealed record CollectionDownloadItem(CollectionMod Mod,string? PackageId,string? PackageVersion,bool Ready);

public static class CollectionDownloads
{
    // Snapshot the original link as well as the installation rule: a pending download must not replace a newer user import.
    public static CollectionDownloadItem[] Plan(ImportedCollection collection,IEnumerable<ModPackage> packages)
    {
        CollectionReader.Validate(collection.Manifest);
        var mods=collection.Manifest.Mods.ToDictionary(m=>m.Id,StringComparer.OrdinalIgnoreCase);
        var states=collection.Entries.ToDictionary(e=>e.ModId,StringComparer.OrdinalIgnoreCase);
        var present=packages.Select(p=>p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result=new List<CollectionDownloadItem>();
        void Visit(string id)
        {
            if(!visited.Add(id))return;
            var mod=mods[id];foreach(var dep in mod.Dependencies)Visit(dep);
            var state=states[id];
            result.Add(new(mod,state.PackageId,state.PackageVersion,state.PackageId!=null&&present.Contains(state.PackageId)&&state.PackageVersion==mod.Version));
        }
        foreach(var state in collection.Entries.Where(e=>e.Selected))Visit(state.ModId);
        return result.ToArray();
    }
    public static bool IsCurrent(CollectionDownloadItem snapshot,ImportedCollection? current)
    {
        var mod=current?.Manifest.Mods.FirstOrDefault(m=>m.Id.Equals(snapshot.Mod.Id,StringComparison.OrdinalIgnoreCase));
        var state=current?.Entries.FirstOrDefault(e=>e.ModId.Equals(snapshot.Mod.Id,StringComparison.OrdinalIgnoreCase));
        return mod!=null&&state?.Selected==true&&state.PackageId==snapshot.PackageId&&state.PackageVersion==snapshot.PackageVersion&&JsonSerializer.Serialize(mod)==JsonSerializer.Serialize(snapshot.Mod);
    }
}
