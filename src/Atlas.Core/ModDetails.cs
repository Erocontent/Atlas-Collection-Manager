using System.Text.Json;
namespace Atlas.Core;

public sealed record ModDetails(string Id,string Category,string Description,string Version,string? SourceUrl,DateTimeOffset ImportedAt,string? Cover,string[] Images,string[] Dependencies);

public sealed class ModDetailsStore
{
    private static readonly object MetadataWriteGate=new();
    private readonly string root;private readonly GameProfile profile;private ImportedCollection[]? collectionSnapshot;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public ModDetailsStore(string libraryRoot,GameProfile profile){root=Path.GetFullPath(libraryRoot);SafePaths.NoLinks(root);this.profile=profile;}
    private static void Id(string id){if(!Guid.TryParseExact(id,"N",out _))throw new InvalidOperationException("Invalid package ID.");}
    private string PackagePath(string id){Id(id);return SafePaths.Resolve(root,"packages/"+id+"/mod.json");}
    private string MetadataPath(string id){Id(id);return SafePaths.Resolve(root,"mod-details/"+id+".json");}
    private ModPackage Current(string id){var path=PackagePath(id);if(!File.Exists(path))throw new InvalidOperationException("Package not found.");return JsonSerializer.Deserialize<ModPackage>(File.ReadAllText(path),Json)??throw new InvalidOperationException("Invalid package.");}
    private ImportedCollection[] Collections(){if(collectionSnapshot!=null)return collectionSnapshot;if(!Directory.Exists(SafePaths.Resolve(root,"collections")))return collectionSnapshot=[];return collectionSnapshot=new CollectionStore(root,profile).List().ToArray();}
    public ModDetails Get(ModPackage package)
    {
        var current=Current(package.Id);var path=PackagePath(current.Id);
        var date=current.ImportedAt??new DateTimeOffset(File.GetLastWriteTimeUtc(path),TimeSpan.Zero);
        var result=new ModDetails(current.Id,current.Category,"","",null,date,null,[],[]);
        try {
            foreach(var collection in Collections()) {
                var entry=collection.Entries.FirstOrDefault(e=>e.PackageId==current.Id);if(entry==null)continue;
                var mod=collection.Manifest.Mods.Single(m=>m.Id==entry.ModId);
                result=result with{Description=mod.Description,Version=entry.PackageVersion??"",SourceUrl=mod.SourceUrl,Dependencies=mod.Dependencies.Select(d=>collection.Manifest.Mods.FirstOrDefault(m=>m.Id==d)?.Name??d).ToArray()};break;
            }
        } catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { }
        try {
            var metadata=MetadataPath(current.Id);if(!File.Exists(metadata))return result;
            if(new FileInfo(metadata).Length>128*1024)return result;
            var saved=JsonSerializer.Deserialize<ModDetails>(File.ReadAllText(metadata),Json);if(saved==null||saved.Id!=current.Id||saved.Images==null||saved.Dependencies==null||saved.ImportedAt==default)return result;
            Validate(saved.Category,saved.Description,saved.Version,saved.SourceUrl);
            var images=saved.Images.Where(p=>MediaPath(p,current.Id)!=null).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
            return saved with{Category=current.Category,Dependencies=result.Dependencies,Images=images,Cover=saved.Cover!=null&&images.Contains(saved.Cover)?saved.Cover:null};
        } catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException){return result;}
    }
    private static void Validate(string category,string description,string version,string? source)
    {
        static void Text(string? value,int limit,bool single=false){if(value==null||value.Length>limit||value.Any(c=>char.IsControl(c)&&(single||c is not ('\n' or '\r' or '\t'))))throw new InvalidOperationException("Invalid metadata text.");}
        Text(category,100,true);if(string.IsNullOrWhiteSpace(category))throw new InvalidOperationException("Category required.");Text(description,16000);Text(version,80,true);
        if(source!=null){Text(source,2048,true);if(!Uri.TryCreate(source,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length!=0||string.IsNullOrWhiteSpace(uri.Host))throw new InvalidOperationException("Source must be HTTPS without credentials.");}
    }
    private void Write(string path,object value){SafePaths.NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);AtomicFiles.Write(path,s=>JsonSerializer.Serialize(s,value,value.GetType(),Json));}
    public ModDetails Save(string id,string category,string description,string version,string? sourceUrl){lock(MetadataWriteGate)return SaveLocked(id,category,description,version,sourceUrl);}
    private ModDetails SaveLocked(string id,string category,string description,string version,string? sourceUrl)
    {
        category=category.Trim();sourceUrl=string.IsNullOrWhiteSpace(sourceUrl)?null:sourceUrl.Trim();Validate(category,description,version,sourceUrl);
        var package=Current(id);var result=Get(package) with{Category=category,Description=description,Version=version,SourceUrl=sourceUrl};
        var writes=new List<(string Path,object Value)>{(PackagePath(id),package with{Category=category}),(MetadataPath(id),result)};
        foreach(var collection in Collections().Where(c=>c.Entries.Any(e=>e.PackageId==id)))writes.Add((SafePaths.Resolve(root,"collections/"+collection.Manifest.Id.ToLowerInvariant()+".json"),collection with{Entries=collection.Entries.Select(e=>e.PackageId==id?e with{Category=category}:e).ToArray()}));
        var snapshots=writes.Select(w=>(w.Path,Bytes:File.Exists(w.Path)?File.ReadAllBytes(w.Path):null)).ToArray();
        try{foreach(var write in writes)Write(write.Path,write.Value);}catch{foreach(var old in snapshots.Reverse())try{if(old.Bytes==null){if(File.Exists(old.Path))File.Delete(old.Path);}else AtomicFiles.Write(old.Path,s=>s.Write(old.Bytes));}catch{}throw;}
        collectionSnapshot=null;return result;
    }
    private string? MediaPath(string? relative,string? id=null)
    {
        try {
            if(relative==null)return null;var safe=SafePaths.Relative(relative);var parts=safe.Split('/');
            if(parts.Length!=3||parts[0]!="mod-media"||!Guid.TryParseExact(parts[1],"N",out _)||(id!=null&&parts[1]!=id)||!parts[2].EndsWith(".png",StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileNameWithoutExtension(parts[2]),"N",out _))return null;
            var path=SafePaths.Resolve(root,safe);if(!File.Exists(path)||new FileInfo(path).Length>20*1024*1024)return null;return path;
        }catch(Exception e) when(e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException){return null;}
    }
    public string? ImagePath(string? relative)=>MediaPath(relative);
    public bool TryAddEmbeddedCover(string id,byte[] normalizedPng)
    {
        lock(MetadataWriteGate){var current=Get(Current(id));if(current.Cover!=null||current.Images.Length>0)return false;AddImageLocked(id,normalizedPng,true);return true;}
    }
    public void AddImage(string id,byte[] normalizedPng,bool cover){lock(MetadataWriteGate)AddImageLocked(id,normalizedPng,cover);}
    private void AddImageLocked(string id,byte[] normalizedPng,bool cover)
    {
        Id(id);Current(id);
        ReadOnlySpan<byte> signature=[137,80,78,71,13,10,26,10];
        if(normalizedPng==null||normalizedPng.Length>20*1024*1024||normalizedPng.Length<signature.Length||!normalizedPng.AsSpan(0,signature.Length).SequenceEqual(signature))throw new InvalidOperationException("Invalid normalized PNG image.");
        var relative=$"mod-media/{id}/{Guid.NewGuid():N}.png";
        var path=SafePaths.Resolve(root,relative);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);SafePaths.NoLinks(path);
            AtomicFiles.Write(path,s=>s.Write(normalizedPng));RecordImage(id,relative,cover);
        }
        catch
        {
            try {var checkedPath=SafePaths.Resolve(root,relative);SafePaths.NoLinks(checkedPath);if(File.Exists(checkedPath))File.Delete(checkedPath);}catch { }
            throw;
        }
    }
    public void RecordImage(string id,string safeRelativePath,bool cover){lock(MetadataWriteGate)RecordImageLocked(id,safeRelativePath,cover);}
    private void RecordImageLocked(string id,string safeRelativePath,bool cover)
    {
        var current=Get(Current(id));if(MediaPath(safeRelativePath,id)==null)throw new InvalidOperationException("Invalid local PNG image.");
        var relative=SafePaths.Relative(safeRelativePath);var images=current.Images.Append(relative).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(images.Length>100)throw new InvalidOperationException("Too many images.");Write(MetadataPath(id),current with{Images=images,Cover=cover?relative:current.Cover});
    }
    public void RemoveImage(string id,string relative){lock(MetadataWriteGate){var current=Get(Current(id));Write(MetadataPath(id),current with{Images=current.Images.Where(p=>p!=relative).ToArray(),Cover=current.Cover==relative?null:current.Cover});}}
}
