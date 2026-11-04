using System.Text;

namespace Atlas.Core;

public sealed record ModVariant(string Name, string? Preview, string[]? Files = null, Dictionary<string, string>? Content = null);

public sealed partial class ModLibrary
{
    private sealed record VariantSource(string Name, string? Parent, string Root, string? Screenshot);
    private sealed record VariantManifest(ModVariant[] Options, int Selected);

    internal static string[]? VariantTargets(ModArchives.Archive archive) => InspectVariants(archive)?.Targets;

    private static (VariantSource[] Choices, Dictionary<string, ModArchives.Entry>[] Payloads, string[] Targets)? InspectVariants(ModArchives.Archive archive)
    {
        var metadata = archive.Files.Where(entry => Path.GetFileName(entry.FullName).Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (metadata.Length is < 2 or > 100) return null;
        var sources = metadata.Select(entry =>
        {
            using var buffer = new MemoryStream();
            long copied = 0;
            entry.CopyTo(buffer, ref copied, 65536);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF').Split('\n'))
            {
                var separator = line.IndexOf('=');
                if (separator > 0) fields.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim().Trim('"'));
            }
            var slash = entry.FullName.LastIndexOf('/');
            return new VariantSource(fields.GetValueOrDefault("name") ?? "", fields.GetValueOrDefault("addonfor"),
                entry.FullName[..(slash + 1)], fields.GetValueOrDefault("screenshot"));
        }).ToArray();
        if (InspectPakVariants(archive, sources) is {} paks) return paks;
        if (InspectNumberedVariants(archive, sources) is {} numbered) return numbered;
        var parents = sources.Where(source => string.IsNullOrWhiteSpace(source.Parent)).ToArray();
        if (sources.Any(source => source.Name.Length is 0 or > 200)) return null;
        var siblings = parents.Length == sources.Length && sources.Select(source =>
            Path.GetDirectoryName(source.Root.TrimEnd('/')) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var choices = siblings ? sources.OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase).ToArray() :
            parents.Length == 1 ? sources.Where(source => Same(source.Parent ?? "", parents[0].Name))
                .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase).Prepend(parents[0]).ToArray() : [];
        if (choices.Length != sources.Length || choices.Any(source => source.Root.Length == 0)) return null;
        var payloads = choices.Select(source => archive.Files
            .Where(entry => entry.FullName.StartsWith(source.Root + "natives/", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.FullName[source.Root.Length..], StringComparer.OrdinalIgnoreCase)).ToArray();
        var targets = payloads[0].Keys.ToArray();
        var siblingMaterials = siblings && payloads.All(files => files.Count == 1 &&
            (files.Keys.Single().Contains(".tex.", StringComparison.OrdinalIgnoreCase) || files.Keys.Single().Contains(".mdf2.", StringComparison.OrdinalIgnoreCase)));
        if (targets.Length == 0 || (!siblingMaterials && !targets.Any(path => payloads.All(files => files.ContainsKey(path))))) return null;
        if (archive.Files.Any(entry => !choices.Any(source => entry.FullName.StartsWith(source.Root, StringComparison.OrdinalIgnoreCase)))) return null;

        return (choices, payloads, ImportAnalysis.Targets(targets, "", false));
    }

    private static (VariantSource[], Dictionary<string, ModArchives.Entry>[], string[])? InspectPakVariants(
        ModArchives.Archive archive, VariantSource[] sources)
    {
        if (sources.Length < 2 || sources.Any(source => source.Root.Length == 0 || source.Name.Length is 0 or > 200) ||
            sources.Select(source => Path.GetDirectoryName(source.Root.TrimEnd('/'))).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return null;
        var payloads = new List<Dictionary<string, ModArchives.Entry>>();
        foreach (var source in sources)
        {
            var files = archive.Files.Where(entry => entry.FullName.StartsWith(source.Root, StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files is not [var pak]) return null;
            var target = "pak_mods/" + Path.GetFileName(pak.FullName);
            payloads.Add(new(StringComparer.OrdinalIgnoreCase) { [target] = pak });
        }
        if (archive.Files.Count(entry => entry.FullName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) != sources.Length) return null;
        return (sources, payloads.ToArray(), payloads[0].Keys.ToArray());
    }

    private static (VariantSource[], Dictionary<string, ModArchives.Entry>[], string[])? InspectNumberedVariants(
        ModArchives.Archive archive, VariantSource[] sources)
    {
        var main = sources.Where(source => source.Name.StartsWith("0) - Main files", StringComparison.OrdinalIgnoreCase)).ToArray();
        var outfit = sources.Where(source => source.Name.StartsWith("1) - ", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (main is not [var baseSource] || outfit is not [var outfitSource] || outfitSource.Parent == null ||
            !Same(baseSource.Parent ?? "", outfitSource.Parent)) return null;
        var optional = sources.Where(source => Same(source.Parent ?? "", "3) - Optional")).ToArray();
        if (optional.Length is < 1 or > 6) return null;
        Dictionary<string, ModArchives.Entry> Files(VariantSource? addOn)
        {
            var files = new Dictionary<string, ModArchives.Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in new[] { baseSource, outfitSource, addOn })
                if (source != null)
                    foreach (var entry in archive.Files.Where(entry => entry.FullName.StartsWith(source.Root + "natives/", StringComparison.OrdinalIgnoreCase)))
                        files[entry.FullName[source.Root.Length..]] = entry;
            return files;
        }
        var baseFiles = Files(null);
        if (baseFiles.Count == 0) return null;
        var choices = new List<VariantSource> { outfitSource with { Name = outfitSource.Parent } };
        var payloads = new List<Dictionary<string, ModArchives.Entry>> { baseFiles };
        var additions = optional.Select(source => Files(source)
            .Where(file => !baseFiles.TryGetValue(file.Key, out var original) || original != file.Value).ToArray()).ToArray();
        for (var mask = 1; mask < 1 << optional.Length; mask++)
        {
            var selected = Enumerable.Range(0, optional.Length).Where(index => (mask & (1 << index)) != 0).ToArray();
            var additionsForChoice = selected.SelectMany(index => additions[index]).ToArray();
            if (additionsForChoice.Length == 0 || additionsForChoice.GroupBy(file => file.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) continue;
            var files = new Dictionary<string, ModArchives.Entry>(baseFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var file in additionsForChoice) files[file.Key] = file.Value;
            var names = string.Join(" + ", selected.Select(index => optional[index].Name));
            choices.Add(outfitSource with { Name = outfitSource.Parent + " + " + names });
            payloads.Add(files);
        }
        return (choices.ToArray(), payloads.ToArray(), baseFiles.Keys.ToArray());
    }

    private ModPackage? ImportVariants(string archivePath, string? name, string category, string destination)
    {
        if (profile.Id != "re4" || destination.Length != 0) return null;
        using var archive = ModArchives.Open(archivePath);
        if (InspectVariants(archive) is not {} group) return null;
        var (choices, payloads, targets) = group;
        var parent = choices[0];
        var id = Guid.NewGuid().ToString("N");
        var root = Package(id);
        var options = new List<ModVariant>();
        long total = 0;
        var stored = new Dictionary<ModArchives.Entry, string>();
        try
        {
            for (var index = 0; index < choices.Length; index++)
            {
                var content = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var variantFiles = ImportAnalysis.Targets(payloads[index].Keys, "", false);
                foreach (var path in variantFiles)
                {
                    var entry = payloads[index][path];
                    if (!stored.TryGetValue(entry, out var relative))
                    {
                        relative = "variant-content/" + stored.Count;
                        var target = SafePaths.Resolve(root, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                        entry.CopyTo(output, ref total);
                        stored.Add(entry, relative);
                    }
                    content.Add(path, relative);
                }
                var source = choices[index];
                var screenshot = archive.Files.FirstOrDefault(entry => Same(entry.FullName, source.Root + source.Screenshot));
                string? preview = null;
                if (screenshot != null && screenshot.Length <= 8 * 1024 * 1024 &&
                    Path.GetExtension(screenshot.FullName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp")
                {
                    preview = "variant-previews/" + index + Path.GetExtension(screenshot.FullName);
                    var path = SafePaths.Resolve(root, preview);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    screenshot.CopyTo(output, ref total, 8 * 1024 * 1024);
                }
                options.Add(new ModVariant(source.Name, preview, variantFiles, content));
            }
            Save(Path.Combine(root, "variants.json"), new VariantManifest(options.ToArray(), 0));
            var package = new ModPackage(id, string.IsNullOrWhiteSpace(name) ? parent.Name : name.Trim(), category, "", targets, 2, "variants", DateTimeOffset.UtcNow);
            Save(Path.Combine(root, "mod.json"), package);
            return package;
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            throw;
        }
    }

    private VariantManifest VariantState(string id) => Read<VariantManifest>(SafePaths.Resolve(Package(id), "variants.json"));

    private ModPackage SelectedVariantPackage(ModPackage package)
    {
        if (package.Kind != "variants") return package;
        var state = VariantState(package.Id);
        return state.Options[state.Selected].Files is {} files ? package with { Files = files } : package;
    }

    internal Func<string, string> PackageContentResolver(ModPackage package)
    {
        var root = Package(package.Id);
        if (package.Kind == "variants")
        {
            var state = VariantState(package.Id);
            if (state.Options[state.Selected].Content is {} content)
                return path => SafePaths.Resolve(root, content[path]);
            root = SafePaths.Resolve(root, "variants/" + state.Selected);
        }
        else root = SafePaths.Resolve(root, "content");
        return path => SafePaths.Resolve(root, path);
    }

    public IReadOnlyList<ModVariant> Variants(string id) => VariantState(id).Options;
    public int SelectedVariant(string id) => VariantState(id).Selected;
    public byte[]? VariantPreview(string id, int index)
    {
        var preview = VariantState(id).Options[index].Preview;
        if (preview == null) return null;
        var path = SafePaths.Resolve(Package(id), preview);
        return File.Exists(path) && new FileInfo(path).Length <= 8 * 1024 * 1024 ? File.ReadAllBytes(path) : null;
    }

    public void SelectVariant(string id, int index, string game)
    {
        NoPending();
        var state = VariantState(id);
        if ((uint)index >= (uint)state.Options.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (state.Selected == index) return;
        var path = SafePaths.Resolve(Package(id), "variants.json");
        var active = Active().SingleOrDefault(mod => mod.Id == id);
        if (active != null)
        {
            game = ValidateGame(game, profile);
            if (!Same(active.Game, game)) throw new InvalidOperationException("The mod is active in a different game folder.");
            Disable(id, game);
        }
        try
        {
            Save(path, state with { Selected = index });
            if (active != null) Enable(id, game);
        }
        catch
        {
            if (!File.Exists(Pending) && !Active().Any(mod => mod.Id == id))
            {
                Save(path, state);
                if (active != null) Enable(id, game);
            }
            throw;
        }
    }
}
