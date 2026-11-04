using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Atlas.Core;

public sealed record CollectionManifest(int SchemaVersion,string Id,string Name,string Author,string Version,string GameId,CollectionMod[] Mods,string Description="");
public sealed record CollectionMod(string Id,string Name,string Version,string? Category,string Description,string? SourceUrl,string? FileName,string? Sha256,bool Recommended,string[] Dependencies,CollectionInstallation Installation,NexusCollectionSource? Nexus=null);
public sealed record NexusCollectionSource(long ModId,long FileId);
public sealed record CollectionInstallation(string Type,string Destination="",string? Character=null);
public sealed record CollectionEntryState(string ModId,bool Selected,string Category,string? PackageId=null,string? PackageVersion=null);
public sealed record ImportedCollection(CollectionManifest Manifest,CollectionEntryState[] Entries);

public static class CollectionReader
{
    public const int MaximumBytes=5*1024*1024;
    internal static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=32};
    public static CollectionManifest Read(string path)
    {
        SafePaths.NoLinks(path);
        using var stream=File.OpenRead(path);
        if(stream.Length>MaximumBytes)throw new InvalidOperationException("Collection exceeds 5 MiB.");
        var bytes=new byte[MaximumBytes+1];var length=0;int read;
        while(length<bytes.Length && (read=stream.Read(bytes,length,bytes.Length-length))>0)length+=read;
        if(length>MaximumBytes)throw new InvalidOperationException("Collection exceeds 5 MiB.");
        try
        {
            using var document=JsonDocument.Parse(bytes.AsMemory(0,length),new(){MaxDepth=32});
            CheckProperties(document.RootElement);
            var node=JsonNode.Parse(document.RootElement.GetRawText())!;
            if(node.AsObject().FirstOrDefault(p=>p.Key.Equals("mods",StringComparison.OrdinalIgnoreCase)).Value is JsonArray mods)foreach(var mod in mods){if(mod is JsonObject obj){if(!obj.Any(p=>p.Key.Equals("description",StringComparison.OrdinalIgnoreCase)))obj["description"]="";if(!obj.Any(p=>p.Key.Equals("dependencies",StringComparison.OrdinalIgnoreCase)))obj["dependencies"]=new JsonArray();}}
            var manifest=node.Deserialize<CollectionManifest>(Json)??throw new InvalidOperationException("Invalid collection.");
            Validate(manifest);return manifest;
        }
        catch(JsonException e){throw new InvalidOperationException("Invalid collection JSON.",e);}
    }
    private static void CheckProperties(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in element.EnumerateObject()){if(!names.Add(p.Name))throw new InvalidOperationException("Duplicate JSON property.");CheckProperties(p.Value);}}
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())CheckProperties(item);
    }
    internal static void Id(string? value){if(value==null || !Regex.IsMatch(value,@"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}\z"))throw new InvalidOperationException("Invalid collection or mod ID.");}
    private static void Text(string? value,int max,bool required=false){if(value==null || value.Length>max || (required && string.IsNullOrWhiteSpace(value)) || value.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t'))throw new InvalidOperationException("Invalid or oversized collection field.");}
    public static void Validate(CollectionManifest m)
    {
        if(m==null || m.SchemaVersion!=1)throw new InvalidOperationException("Unsupported collection schema.");
        Id(m.Id);Text(m.Name,200,true);Text(m.Author,200,true);Text(m.Version,80,true);Text(m.Description,16000);GameCatalog.Get(m.GameId);
        if(m.Mods==null || m.Mods.Length>1000)throw new InvalidOperationException("Collection exceeds 1,000 mods.");
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var mod in m.Mods)
        {
            if(mod==null)throw new InvalidOperationException("Invalid mod.");Id(mod.Id);if(!ids.Add(mod.Id))throw new InvalidOperationException("Duplicate mod ID.");
            Text(mod.Name,200,true);Text(mod.Version,80,true);Text(mod.Description,16000);if(mod.Category!=null)Text(mod.Category,100);
            if(mod.SourceUrl!=null){Text(mod.SourceUrl,2048);if(!Uri.TryCreate(mod.SourceUrl,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length!=0||string.IsNullOrWhiteSpace(uri.Host))throw new InvalidOperationException("Source must be an HTTPS URL without credentials.");}
            if(mod.Nexus is {} nexus)
            {
                if(nexus.ModId<=0||nexus.FileId<=0)throw new InvalidOperationException("Invalid Nexus collection identifiers.");
                if(mod.SourceUrl!=null)
                {
                    var source=new Uri(mod.SourceUrl);var expected="/"+NexusGames.Domain(m.GameId)+"/mods/"+nexus.ModId;
                    if(source.Host is not ("www.nexusmods.com" or "nexusmods.com")||!source.IsDefaultPort||!source.AbsolutePath.TrimEnd('/').Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Nexus source does not match the collection game and mod.");
                }
            }
            if(mod.FileName!=null){Text(mod.FileName,255,true);SafePaths.Relative(mod.FileName);if(mod.FileName.Contains('/')||mod.FileName.Contains('\\'))throw new InvalidOperationException("File name must be a single safe name.");}
            if(mod.Sha256!=null && !Regex.IsMatch(mod.Sha256,@"\A[0-9a-fA-F]{64}\z"))throw new InvalidOperationException("Invalid SHA256.");
            if(mod.Installation==null || mod.Installation.Type is not ("game-files" or "autolink-skin" or "reshade"))throw new InvalidOperationException("Unsupported installation type.");
            if(mod.Installation.Character!=null&&(mod.Installation.Type!="autolink-skin"||!AutoLinkCharacters.Names.Contains(mod.Installation.Character)))throw new InvalidOperationException("Invalid AutoLink character.");
            Text(mod.Installation.Destination,1024);SafePaths.Relative(mod.Installation.Destination,true);
            if(mod.Installation.Type=="autolink-skin" && m.GameId!="doa5")throw new InvalidOperationException("AutoLink skins require DOA5.");
            if(mod.Installation.Type=="autolink-skin" && mod.Installation.Destination is not ("" or "AutoLink"))throw new InvalidOperationException("AutoLink destination must be AutoLink.");
            if(mod.Installation.Type=="reshade" && mod.Installation.Destination!="")throw new InvalidOperationException("ReShade destination must be the game root.");
            if(mod.Dependencies==null || mod.Dependencies.Length>1000)throw new InvalidOperationException("Invalid dependencies.");
        }
        var mods=m.Mods.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);var visiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var done=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id){if(done.Contains(id))return;if(!visiting.Add(id))throw new InvalidOperationException("Collection dependency cycle.");foreach(var dep in mods[id].Dependencies){Id(dep);if(!mods.ContainsKey(dep))throw new InvalidOperationException("Unknown dependency.");Visit(dep);}visiting.Remove(id);done.Add(id);}
        foreach(var id in ids)Visit(id);
        if(JsonSerializer.SerializeToUtf8Bytes(m,Json).Length>MaximumBytes)throw new InvalidOperationException("Collection exceeds 5 MiB.");
    }
}

public sealed class CollectionStore
{
    private readonly string root;private readonly GameProfile profile;
    public CollectionStore(string libraryRoot,GameProfile profile){this.profile=profile;SafePaths.NoLinks(libraryRoot);root=SafePaths.Resolve(libraryRoot,"collections");Directory.CreateDirectory(root);}
    private string FilePath(string id){CollectionReader.Id(id);return SafePaths.Resolve(root,id.ToLowerInvariant()+".json");}
    private void Save(ImportedCollection collection){var path=FilePath(collection.Manifest.Id);AtomicFiles.Write(path,stream=>JsonSerializer.Serialize(stream,collection,CollectionReader.Json));}
    public IReadOnlyList<ImportedCollection> List(){SafePaths.NoLinks(root);return Directory.EnumerateFiles(root,"*.json").Select(path=>{SafePaths.NoLinks(path);return Load(path);}).OrderBy(x=>x.Manifest.Name).ToArray();}
    private ImportedCollection Load(string path){SafePaths.NoLinks(path);if(new FileInfo(path).Length>12*1024*1024)throw new InvalidOperationException("Oversized collection record.");var item=JsonSerializer.Deserialize<ImportedCollection>(File.ReadAllText(path),CollectionReader.Json)??throw new InvalidOperationException("Invalid collection record.");CollectionReader.Validate(item.Manifest);if(item.Manifest.GameId!=profile.Id)throw new InvalidOperationException("Collection belongs to another game.");ValidateEntries(item);return item;}
    private static void ValidateEntries(ImportedCollection item)
    {
        if(item.Entries==null || item.Entries.Length!=item.Manifest.Mods.Length)throw new InvalidOperationException("Invalid collection entry state.");
        var remaining=item.Manifest.Mods.Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in item.Entries)
        {
            if(entry==null || !remaining.Remove(entry.ModId) || string.IsNullOrWhiteSpace(entry.Category) || entry.Category.Length>100 || (entry.PackageId!=null&&!Guid.TryParseExact(entry.PackageId,"N",out _)) || entry.PackageVersion?.Length>80)throw new InvalidOperationException("Invalid collection entry state.");
        }
    }
    public ImportedCollection? Find(string id){var path=FilePath(id);return File.Exists(path)?Load(path):null;}
    public ImportedCollection Import(CollectionManifest manifest,bool importCategories,IEnumerable<string> selectedModIds)
    {
        CollectionReader.Validate(manifest);if(manifest.GameId!=profile.Id)throw new InvalidOperationException("Collection belongs to another game.");
        var selected=selectedModIds.ToHashSet(StringComparer.OrdinalIgnoreCase);if(selected.Any(id=>!manifest.Mods.Any(m=>string.Equals(m.Id,id,StringComparison.OrdinalIgnoreCase))))throw new InvalidOperationException("Unknown selected mod.");
        var previous=Find(manifest.Id)?.Entries.ToDictionary(e=>e.ModId,StringComparer.OrdinalIgnoreCase);
        var entries=manifest.Mods.Select(m=>previous!=null&&previous.TryGetValue(m.Id,out var old)?old with{ModId=m.Id,Selected=selected.Contains(m.Id)}:new CollectionEntryState(m.Id,selected.Contains(m.Id),importCategories&&!string.IsNullOrWhiteSpace(m.Category)?m.Category:"Uncategorized")).ToArray();
        var result=new ImportedCollection(manifest,entries);Save(result);return result;
    }
    // Collection records own references only; packages and game deployments have independent lifetimes.
    public void SelectNexusUpdate(string id, CollectionMod expected, NexusFile update)
    {
        var current = Find(id) ?? throw new InvalidOperationException("Collection not found.");
        var mod = current.Manifest.Mods.Single(m => m.Id == expected.Id);
        if (mod.Nexus is not {} source || JsonSerializer.Serialize(mod) != JsonSerializer.Serialize(expected))
            throw new InvalidOperationException("The collection changed. Check for updates again.");
        if (update.FileId <= 0 || update.FileId == source.FileId || string.IsNullOrWhiteSpace(update.Version) || !ModArchives.IsSupported(update.FileName))
            throw new InvalidOperationException("Invalid Nexus update.");

        var replacement = mod with
        {
            Nexus = source with { FileId = update.FileId },
            Version = update.Version,
            FileName = update.FileName,
            Sha256 = null
        };
        var manifest = current.Manifest with { Mods = current.Manifest.Mods.Select(m => m.Id == mod.Id ? replacement : m).ToArray() };
        CollectionReader.Validate(manifest);
        var entries = current.Entries.Select(entry =>
            entry.ModId == mod.Id && entry.PackageVersion == update.Version ? entry with { PackageVersion = null } : entry).ToArray();
        Save(current with { Manifest = manifest, Entries = entries });
    }

    private string FailurePath(string id)
    {
        CollectionReader.Id(id);
        return SafePaths.Resolve(root,id.ToLowerInvariant()+".progress");
    }

    public HashSet<string> ReadFailures(string id) => ReadFailureDetails(id).Keys.ToHashSet();

    public Dictionary<string, string> ReadFailureDetails(string id)
    {
        try
        {
            var path = FailurePath(id);
            if (File.Exists(path) && new FileInfo(path).Length <= 2 * 1024 * 1024)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    return (document.RootElement.Deserialize<HashSet<string>>() ?? []).ToDictionary(key => key, _ => "");
                return document.RootElement.Deserialize<Dictionary<string, string>>() ?? [];
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) { }
        return [];
    }

    public void SetFailure(string id, string fingerprint, bool failed, string? detail = null)
    {
        try
        {
            var entries = ReadFailureDetails(id);
            if (failed) entries[fingerprint] = detail == null ? "" : detail[..Math.Min(detail.Length, 512)];
            else if (!entries.Remove(fingerprint)) return;
            AtomicFiles.Write(FailurePath(id), stream => JsonSerializer.Serialize(stream, entries));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    public void Delete(string id)
    {
        var path=FilePath(id);SafePaths.NoLinks(path);var progress=FailurePath(id);File.Delete(progress);File.Delete(path);
    }
    public void SaveSelection(string collectionId,string modId,bool selected){var item=Find(collectionId)??throw new InvalidOperationException("Collection not found.");if(!item.Entries.Any(e=>e.ModId==modId))throw new InvalidOperationException("Collection mod not found.");Save(item with{Entries=item.Entries.Select(e=>e.ModId==modId?e with{Selected=selected}:e).ToArray()});}
    public ModPackage AttachZip(string collectionId,string modId,string zip,ModLibrary library)
    {
        var item=Find(collectionId)??throw new InvalidOperationException("Collection not found.");var mod=item.Manifest.Mods.Single(m=>m.Id==modId);var entry=item.Entries.Single(e=>e.ModId==modId);
        library.EnsureReady();if(entry.PackageId!=null&&library.GetActive().Any(a=>a.Id==entry.PackageId))throw new InvalidOperationException("Disable the associated package before replacing it.");
        SafePaths.NoLinks(zip);
        // Keep a read-only Windows lease until import finishes, so the verified ZIP cannot be replaced or edited between reads.
        using var zipLease=new FileStream(zip,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(mod.Sha256!=null){if(!Convert.ToHexString(SHA256.HashData(zipLease)).Equals(mod.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ZIP SHA256 does not match the collection.");}
        ModPackage package;
        if(mod.Installation.Type=="reshade"){var plan=ReShadePackage.Inspect(zip)??throw new InvalidOperationException("ZIP is not a supported ReShade package.");package=library.ImportReShade(zip,mod.Name,plan);}
        else {
            var skin=mod.Installation.Type=="autolink-skin";var canonical=skin&&(mod.Installation.Character!=null||ImportAnalysis.Inspect(zip,GameCatalog.Get("doa5")).Kind==ArchiveImportKind.AutoLinkSkin);
            package=library.ImportZip(zip,mod.Name,entry.Category,mod.Installation.Destination,skin,mod.Installation.Character,canonical);
        }
        try{Save(item with{Entries=item.Entries.Select(e=>e.ModId==modId?e with{PackageId=package.Id,PackageVersion=mod.Version}:e).ToArray()});}
        catch{library.Delete(package.Id);throw;}
        return package;
    }
    public void EnsureCanEnable(string packageId,ModLibrary library,string gameFolder)
    {
        var game=Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder));var active=library.GetActive();var packages=library.List().Select(p=>p.Id).ToHashSet();
        foreach(var item in List())foreach(var entry in item.Entries.Where(e=>e.PackageId==packageId))
        {
            var mod=item.Manifest.Mods.Single(m=>m.Id==entry.ModId);
            foreach(var dependency in mod.Dependencies){var linked=item.Entries.Single(e=>string.Equals(e.ModId,dependency,StringComparison.OrdinalIgnoreCase));if(linked.PackageId==null||!packages.Contains(linked.PackageId)||!active.Any(a=>a.Id==linked.PackageId&&string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a.Game)),game,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Required collection dependency is not active in this game folder: "+dependency);}
        }
    }
}
