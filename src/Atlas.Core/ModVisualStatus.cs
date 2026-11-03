namespace Atlas.Core;

public sealed record ModStatus(string[] Conflicts, string[] MissingDependencies)
{
    public bool HasConflict => Conflicts.Length != 0;
    public bool HasMissingDependencies => MissingDependencies.Length != 0;
}

/// <summary>Computes display warnings from snapshots without reading or changing disk state.</summary>
public static class ModVisualStatus
{
    private sealed class TargetNode
    {
        public Dictionary<string, TargetNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Descendants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, ModStatus> Evaluate(IReadOnlyList<ModPackage> packages,
        IReadOnlyList<ActiveMod> active, IReadOnlyList<ImportedCollection> collections, string gameFolder)
    {
        if (packages == null || active == null || collections == null) throw new InvalidOperationException("Missing visual status metadata.");
        var folder = gameFolder == "" ? null : Folder(gameFolder);
        var byId = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
            if (package == null || string.IsNullOrWhiteSpace(package.Id) || package.Files == null || !byId.TryAdd(package.Id, package))
                throw new InvalidOperationException("Invalid or duplicate package metadata for visual status.");
        var tree = new TargetNode();
        var activeHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in active)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || mod.Files == null) throw new InvalidOperationException("Invalid active mod metadata.");
            if (folder == null || !StringComparer.OrdinalIgnoreCase.Equals(Folder(mod.Game), folder)) continue;
            activeHere.Add(mod.Id);
            foreach (var file in mod.Files)
            {
                if (file == null) throw new InvalidOperationException("Invalid active file metadata.");
                var node = tree;
                foreach (var segment in Relative(file.Path).Split('/'))
                {
                    node.Descendants.Add(mod.Id);
                    if (!node.Children.TryGetValue(segment, out var child)) node.Children.Add(segment, child = new TargetNode());
                    node = child;
                }
                node.Descendants.Add(mod.Id);
                node.Owners.Add(mod.Id);
            }
        }
        var missing = byId.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var collection in collections)
        {
            if (collection?.Manifest?.Mods == null || collection.Entries == null) throw new InvalidOperationException("Invalid collection metadata for visual status.");
            var mods = new Dictionary<string, CollectionMod>(StringComparer.OrdinalIgnoreCase);
            var entries = new Dictionary<string, CollectionEntryState>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in collection.Manifest.Mods)
                if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || mod.Dependencies == null || !mods.TryAdd(mod.Id, mod)) throw new InvalidOperationException("Invalid collection mod metadata.");
            foreach (var entry in collection.Entries)
                if (entry == null || string.IsNullOrWhiteSpace(entry.ModId) || !entries.TryAdd(entry.ModId, entry)) throw new InvalidOperationException("Invalid collection link metadata.");
            foreach (var entry in entries.Values)
            {
                if (entry.PackageId == null || !missing.TryGetValue(entry.PackageId, out var names)) continue;
                if (!mods.TryGetValue(entry.ModId, out var mod)) throw new InvalidOperationException("Collection link references an unknown mod.");
                foreach (var dependency in mod.Dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependency)) throw new InvalidOperationException("Invalid collection dependency metadata.");
                    if (!entries.TryGetValue(dependency, out var link) || link.PackageId == null || !byId.ContainsKey(link.PackageId) || !activeHere.Contains(link.PackageId))
                        names.Add(mods.TryGetValue(dependency, out var dependencyMod) ? dependencyMod.Name : dependency);
                }
            }
        }
        var result = new Dictionary<string, ModStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destination = Relative(package.Destination, true);
            foreach (var file in package.Files)
            {
                var path = Relative(file);
                if (destination.Length != 0) path = destination + "/" + path;
                TargetNode? node = tree;
                foreach (var segment in path.Split('/'))
                {
                    owners.UnionWith(node.Owners);
                    if (!node.Children.TryGetValue(segment, out node)) break;
                }
                if (node != null) owners.UnionWith(node.Descendants);
            }
            owners.Remove(package.Id);
            result.Add(package.Id, new(owners.Select(id => byId.TryGetValue(id, out var other) ? other.Name : id).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                missing[package.Id].Order(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModStatus>(result);
    }

    private static string Relative(string? path, bool allowEmpty = false)
    {
        if (path == null) throw new InvalidOperationException("Missing visual status file path.");
        return SafePaths.Relative(path, allowEmpty);
    }

    private static string Folder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Missing visual status game folder.");
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidOperationException("Invalid visual status game folder.", error); }
    }
}
