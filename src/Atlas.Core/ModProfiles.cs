using System.Security.Cryptography;
using System.Text.Json;
namespace Atlas.Core;

public sealed record ModProfile(string Id, string Name, string[] ModIds, string? Preset = null);
public sealed class ProfileFileConflict(ModPackage first, ModPackage second, string firstPath, string secondPath)
    : InvalidOperationException($"File conflict between {first.Name} and {second.Name}.\n{firstPath}\n{secondPath}")
{
    public ModPackage First { get; } = first;
    public ModPackage Second { get; } = second;
}

public sealed record CollectionApplyProgress(int Completed, int Total, string PackageId);

public sealed record ProfilePlan(ModProfile Profile, string[] Enable, string[] Disable, string[] Keep, string? Preset);

public sealed class ModProfileStore
{
    private readonly string root;
    private readonly GameProfile profile;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Applied(string Game, ModProfile Profile);
    private sealed record Guard(string Path, string? Hash, string? NextHash = null, bool AllowsNext = false);
    private sealed record SwitchJournal(string Game, string[] Before, string[] After, byte[]? Ini, Guard[] Guards, Applied? BeforeApplied = null);
    public ModProfileStore(string libraryRoot, GameProfile profile)
    {
        root = Path.GetFullPath(libraryRoot); this.profile = profile; SafePaths.NoLinks(root); Directory.CreateDirectory(root);
    }
    private string Metadata => SafePaths.Resolve(root, "profiles.json");
    private string Pending => SafePaths.Resolve(root, "profile-pending.json");
    private string CurrentFile => SafePaths.Resolve(root, "profile-current.json");
    public bool HasPending => File.Exists(Pending);
    private static bool Same(string a, string b) => string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static string Folder(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Hash(string path) { SafePaths.NoLinks(path); using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s)); }
    private static void Id(string id) { if(!Guid.TryParseExact(id,"N",out _))throw new InvalidOperationException("Invalid profile or package ID."); }
    private static ModProfile Validate(ModProfile p)
    {
        Id(p.Id);if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>200||p.Name.Any(char.IsControl))throw new InvalidOperationException("Profile name must contain 1–200 characters.");
        if(p.ModIds==null||p.ModIds.Length>1000)throw new InvalidOperationException("Invalid profile packages.");foreach(var id in p.ModIds)Id(id);
        if(p.ModIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.ModIds.Length)throw new InvalidOperationException("Duplicate profile package.");
        return p with {Name=p.Name.Trim(),ModIds=p.ModIds.ToArray(),Preset=p.Preset==null?null:SafePaths.Relative(p.Preset)};
    }
    private T Read<T>(string path) {SafePaths.NoLinks(path);if(new FileInfo(path).Length>16*1024*1024)throw new InvalidOperationException("Oversized profile record.");return JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new InvalidOperationException("Invalid profile record.");}
    private void Save<T>(string path,T value)=>AtomicFiles.Write(path,s=>JsonSerializer.Serialize(s,value,Json));
    private void Ready(){if(HasPending)throw new InvalidOperationException("Recover the pending profile switch first.");}
    public IReadOnlyList<ModProfile> List() => File.Exists(Metadata)?Read<ModProfile[]>(Metadata).Select(Validate).OrderBy(p=>p.Name).ToArray():[];
    public ModProfile Create(string name,string[] modIds,string? preset=null){Ready();var p=Validate(new(Guid.NewGuid().ToString("N"),name,modIds,preset));Save(Metadata,List().Append(p).ToArray());return p;}
    public ModProfile Update(string id,string name,string[] mods,string? preset=null){Ready();Id(id);var items=List();if(!items.Any(p=>p.Id==id))throw new InvalidOperationException("Profile not found.");var p=Validate(new(id,name,mods,preset));Save(Metadata,items.Select(old=>old.Id==id?p:old).ToArray());return p;}
    public ModProfile Duplicate(string id,string name){Id(id);var p=List().SingleOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Profile not found.");return Create(name,p.ModIds,p.Preset);}
    public void Delete(string id){Ready();Id(id);Save(Metadata,List().Where(p=>p.Id!=id).ToArray());}
    private Applied[] AppliedProfiles()=>File.Exists(CurrentFile)?Read<Applied[]>(CurrentFile):[];
    public ModProfile? Current(string gameFolder){var game=Folder(gameFolder);var applied=AppliedProfiles().SingleOrDefault(a=>Same(a.Game,game))?.Profile;return applied==null?null:List().SingleOrDefault(p=>p.Id==applied.Id);}
    private string? Default(string game)
    {
        var selected=ReShadePresets.ReadDefault(game);if(string.IsNullOrWhiteSpace(selected))return null;selected=selected.Trim().Trim('"');
        if(selected.StartsWith("./")||selected.StartsWith(".\\"))selected=selected[2..];
        if(Path.IsPathRooted(selected))selected=Path.GetRelativePath(game,selected);
        try{return SafePaths.Relative(selected);}catch(InvalidOperationException){return selected;}
    }
    public bool IsCurrentModified(string gameFolder,ModLibrary library)
    {
        var game=Folder(gameFolder);var current=AppliedProfiles().SingleOrDefault(a=>Same(a.Game,game))?.Profile;if(current==null)return false;
        var latest=List().SingleOrDefault(p=>p.Id==current.Id);if(latest==null||!latest.ModIds.ToHashSet().SetEquals(current.ModIds)||!Same(latest.Preset??"",current.Preset??""))return true;
        var active=library.GetActive().Where(a=>Same(a.Game,game)).ToArray();
        if(!active.Select(a=>a.Id).ToHashSet().SetEquals(current.ModIds)||!Same(Default(game)??"",current.Preset??""))return true;
        return false;
    }
    private static string Target(ModPackage mod,string file)=>SafePaths.Relative(mod.Destination.Length==0?file:mod.Destination+"/"+file);
    public string[] AvailablePresets(string[] modIds,ModLibrary library)
    {
        foreach(var id in modIds)Id(id);return library.List().Where(m=>modIds.Contains(m.Id)).SelectMany(m=>m.Files.Where(f=>ReShadePackage.IsPresetFile(library.PackageContentResolver(m)(f))).Select(f=>Target(m,f))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public ModProfile Capture(string name,string gameFolder,ModLibrary library)
    {
        Ready();var game=ModLibrary.ValidateGame(gameFolder,profile);var ids=library.GetActive().Where(a=>Same(a.Game,game)).Select(a=>a.Id).ToArray();var preset=Default(game);return Create(name,ids,preset!=null&&AvailablePresets(ids,library).Contains(preset,StringComparer.OrdinalIgnoreCase)?preset:null);
    }
    private string[] Order(IEnumerable<string> ids,CollectionStore collections,bool validate)
    {
        var wanted=ids.ToHashSet(StringComparer.OrdinalIgnoreCase);var edges=wanted.ToDictionary(x=>x,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var collection in collections.List())foreach(var entry in collection.Entries.Where(e=>e.PackageId!=null&&wanted.Contains(e.PackageId)))
        foreach(var dep in collection.Manifest.Mods.Single(m=>Same(m.Id,entry.ModId)).Dependencies)
        {
            var linked=collection.Entries.Single(e=>Same(e.ModId,dep)).PackageId;
            if(linked==null||!wanted.Contains(linked)){if(validate)throw new InvalidOperationException("Required dependency is missing from profile: "+dep);continue;}edges[entry.PackageId!].Add(linked);
        }
        var done=new HashSet<string>();var visiting=new HashSet<string>();var result=new List<string>();
        void Visit(string id){if(done.Contains(id))return;if(!visiting.Add(id))throw new InvalidOperationException("Profile dependency cycle.");foreach(var dep in edges[id])Visit(dep);visiting.Remove(id);done.Add(id);result.Add(id);}
        foreach(var id in wanted)Visit(id);return result.ToArray();
    }
    public ProfilePlan Preview(string id,string gameFolder,ModLibrary library,CollectionStore collections)
    {
        Ready();Id(id);var p=List().SingleOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Profile not found.");return PreviewSelection(p,gameFolder,library,collections);
    }
    public ProfilePlan PreviewCollection(string id, string gameFolder, ModLibrary library, CollectionStore collections, bool deactivate = false)
    {
        var collection = collections.Find(id) ?? throw new InvalidOperationException("Collection not found.");
        var requested = CollectionDownloads.Plan(collection, library.List());
        if (!deactivate && (requested.Length == 0 || requested.Any(item => !item.Ready)))
            throw new InvalidOperationException("The collection must be ready before activation.");
        var game = ModLibrary.ValidateGame(gameFolder, profile);
        var collectionPackages = collections.List().SelectMany(item => item.Entries).Select(entry => entry.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var standalone = library.GetActive().Where(item => Same(item.Game, game) && !collectionPackages.Contains(item.Id)).Select(item => item.Id);
        var selected = requested.Select(item => item.PackageId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deactivate)
        {
            selected = collection.Entries.Select(entry => entry.PackageId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeIds = library.GetActive().Where(item => Same(item.Game, game)).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var other in collections.List().Where(item => !Same(item.Manifest.Id, id)))
            {
                var otherPlan = CollectionDownloads.Plan(other, library.List());
                if (otherPlan.Length > 0 && otherPlan.All(item => item.Ready && activeIds.Contains(item.PackageId!)))
                    selected.ExceptWith(otherPlan.Select(item => item.PackageId!));
            }
        }
        var desired = deactivate
            ? library.GetActive().Where(item => Same(item.Game, game) && !selected.Contains(item.Id)).Select(item => item.Id).ToArray()
            : selected.Concat(standalone).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return PreviewSelection(new ModProfile(Guid.NewGuid().ToString("N"), collection.Manifest.Name, desired), game, library, collections);
    }

    public void ApplyCollection(string id, ProfilePlan plan, string gameFolder, ModLibrary library, CollectionStore collections, bool deactivate = false, IProgress<CollectionApplyProgress>? progress = null)
    {
        var fresh = PreviewCollection(id, gameFolder, library, collections, deactivate);
        if (!fresh.Profile.ModIds.ToHashSet().SetEquals(plan.Profile.ModIds))
            throw new InvalidOperationException("The collection changed. Review it again before activating.");
        ApplyValidatedSelection(plan, fresh, gameFolder, library, collections, false, progress);
    }

    internal ProfilePlan PreviewSelection(ModProfile p,string gameFolder,ModLibrary library,CollectionStore collections)
    {
        Ready();library.EnsureReady();p=Validate(p);var game=ModLibrary.ValidateGame(gameFolder,profile);
        var packages=library.List().ToDictionary(m=>m.Id);foreach(var wanted in p.ModIds)if(!packages.ContainsKey(wanted))throw new InvalidOperationException("Profile package is missing: "+wanted);
        var all=library.GetActive();if(all.Any(a=>p.ModIds.Contains(a.Id)&&!Same(a.Game,game)))throw new InvalidOperationException("A profile package is active in another game folder.");
        var active=all.Where(a=>Same(a.Game,game)).ToArray();
        foreach(var a in active)
        {
            if(!packages.TryGetValue(a.Id,out var activePackage))throw new InvalidOperationException("Active package metadata is missing: "+a.Id);
            var content = library.PackageContentResolver(activePackage);
            foreach(var file in activePackage.Files)if(!File.Exists(content(file)))throw new InvalidOperationException("Active package content required for rollback is missing: "+file);
        }
        foreach(var a in active)foreach(var f in a.Files)
        {
            var target=SafePaths.Resolve(game,f.Path);if(!File.Exists(target))throw new InvalidOperationException("Managed file is missing: "+f.Path);
            if(Hash(target)!=f.Hash&&(!packages.TryGetValue(a.Id,out var pkg)||pkg.Kind!="reshade"||pkg.Destination.Length!=0||!ReShadePackage.IsPresetFile(target)))throw new InvalidOperationException("Managed file was changed externally: "+f.Path);
            if(f.HadOriginal&&!File.Exists(SafePaths.Resolve(root,"packages/"+a.Id+"/originals/"+f.Path)))throw new InvalidOperationException("Original backup is missing: "+f.Path);
        }
        var paths=new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);var removed=active.Where(a=>!p.ModIds.Contains(a.Id)).SelectMany(a=>a.Files).ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var mod in p.ModIds.Select(id=>packages[id]))
        {
        var content = library.PackageContentResolver(mod);
        foreach(var file in mod.Files)
        {
            var source=content(file);if(!File.Exists(source))throw new InvalidOperationException("Package content is missing: "+file);
            var relative=Target(mod,file);var target=SafePaths.Resolve(game,relative);
            if(profile.Executables.Any(e=>Same(e,relative))||Same(relative,"ReShade.ini"))throw new InvalidOperationException("Protected game file: "+relative);
            var overlap = paths.FirstOrDefault(other => Same(other.Key, relative) || other.Key.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(other.Key + "/", StringComparison.OrdinalIgnoreCase));
            if (overlap.Value != null) throw new ProfileFileConflict(overlap.Value, mod, overlap.Key, relative);
            paths.Add(relative, mod);
            if(Directory.Exists(target))throw new InvalidOperationException("Directory blocks target: "+relative);
            for(var parent=Path.GetDirectoryName(target);parent!=null&&!Same(parent,game);parent=Path.GetDirectoryName(parent))if(File.Exists(parent))
            {
                var rel=Path.GetRelativePath(game,parent).Replace('\\','/');if(!removed.TryGetValue(rel,out var old)||old.HadOriginal)throw new InvalidOperationException("File blocks target folder: "+rel);
            }
            if(mod.Kind=="reshade"&&ReShadePackage.Proxies.Contains(relative,StringComparer.OrdinalIgnoreCase))
            {
                if(!ReShadePackage.RuntimeMatchesGame(source,GameLauncher.Prepare(profile,game).FileName))throw new InvalidOperationException("ReShade runtime architecture mismatch.");
                if(File.Exists(target)&&!removed.ContainsKey(relative)&&!ReShadePackage.IsRuntime(target))throw new InvalidOperationException("External runtime conflicts with profile.");
            }
        }
        if(p.Preset!=null&&!AvailablePresets(p.ModIds,library).Contains(p.Preset,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Profile preset is missing or invalid.");
        }
        var order=Order(p.ModIds,collections,true);var oldOrder=Order(active.Select(a=>a.Id),collections,false);var activeIds=active.Select(a=>a.Id).ToHashSet();
        return new(p,order.Where(id=>!activeIds.Contains(id)).ToArray(),oldOrder.Reverse().Where(id=>!p.ModIds.Contains(id)).ToArray(),order.Where(activeIds.Contains).ToArray(),p.Preset);
    }
    private static byte[] IniBytes(List<string> lines) => System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine,lines)+Environment.NewLine);
    private static void PutIni(List<string> lines,string key,string? value)
    {
        var general=false;var insertion=-1;
        for(var i=0;i<lines.Count;i++)
        {
            var text=lines[i].Trim();if(text.StartsWith('[')&&text.EndsWith(']')){general=Same(text,"[GENERAL]");if(general)insertion=i+1;continue;}
            var equals=lines[i].IndexOf('=');if(general&&equals>=0&&Same(lines[i][..equals].Trim(),key)){lines.RemoveAt(i--);continue;}if(general)insertion=i+1;
        }
        if(value==null)return;if(insertion<0){if(lines.Count>0)lines.Add("");lines.Add("[GENERAL]");insertion=lines.Count;}lines.Insert(insertion,key+"="+value);
    }
    private string? PredictedIni(string game,string? preset,ActiveMod? disabling=null)
    {
        var path=SafePaths.Resolve(game,"ReShade.ini");if(!File.Exists(path)&&preset==null)return null;
        var lines=File.Exists(path)?File.ReadAllLines(path).ToList():new List<string>();var changed=false;
        if(disabling!=null)
        {
            var general=false;
            foreach(var line in lines.ToArray())
            {
                var text=line.Trim();if(text.StartsWith('[')&&text.EndsWith(']')){general=Same(text,"[GENERAL]");continue;}
                var eq=line.IndexOf('=');if(!general||eq<0)continue;var key=line[..eq].Trim();if(!Same(key,"PresetPath")&&!Same(key,"StartupPresetPath"))continue;
                var value=line[(eq+1)..].Trim().Trim('"');if(string.IsNullOrWhiteSpace(value))continue;if(value.StartsWith("./")||value.StartsWith(".\\"))value=value[2..];
                try {if(Path.IsPathRooted(value))value=Path.GetRelativePath(game,value);value=SafePaths.Relative(value);var removed=disabling.Files.SingleOrDefault(f=>Same(f.Path,value));var exists=removed!=null?removed.HadOriginal:File.Exists(SafePaths.Resolve(game,value));if(!exists){PutIni(lines,key,null);changed=true;}}catch(InvalidOperationException){}
            }
            return changed?Convert.ToHexString(SHA256.HashData(IniBytes(lines))):Hash(path);
        }
        PutIni(lines,"PresetPath",preset==null?null:".\\"+preset.Replace('/','\\'));PutIni(lines,"StartupPresetPath",preset==null?null:".\\"+preset.Replace('/','\\'));
        if(preset!=null)foreach(var (key,standard) in new[]{("EffectSearchPaths",@".\reshade-shaders\Shaders\**"),("TextureSearchPaths",@".\reshade-shaders\Textures\**")})
        {
            var general=false;string? existing=null;foreach(var line in lines){var t=line.Trim();if(t.StartsWith('[')&&t.EndsWith(']')){general=Same(t,"[GENERAL]");continue;}var eq=line.IndexOf('=');if(general&&eq>=0&&Same(line[..eq].Trim(),key)){existing=line[(eq+1)..].Trim();break;}}
            var parts=(existing??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList();if(!parts.Any(p=>Same(p.Replace('/','\\'),standard)))parts.Add(standard);PutIni(lines,key,string.Join(',',parts));
        }
        return Convert.ToHexString(SHA256.HashData(IniBytes(lines)));
    }
    private SwitchJournal Intent(SwitchJournal journal,ModLibrary library,string? id,bool enable,string? preset=null)
    {
        var expected=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        if(id!=null)
        {
            if(enable){var mod=library.List().Single(m=>m.Id==id);var content = library.PackageContentResolver(mod);foreach(var file in mod.Files)expected[Target(mod,file)]=Hash(content(file));}
            else {var active=library.GetActive().Single(a=>a.Id==id);foreach(var f in active.Files)expected[f.Path]=f.HadOriginal?Hash(SafePaths.Resolve(root,"packages/"+id+"/originals/"+f.Path)):null;expected["ReShade.ini"]=PredictedIni(journal.Game,null,active);}
        }
        else expected["ReShade.ini"]=PredictedIni(journal.Game,preset);
        return journal with{Guards=journal.Guards.Select(g=>expected.TryGetValue(g.Path,out var hash)?g with{NextHash=hash,AllowsNext=true}:g).ToArray()};
    }
    private Guard[] Snapshot(string game,IEnumerable<string> paths)=>paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>new Guard(p,File.Exists(SafePaths.Resolve(game,p))?Hash(SafePaths.Resolve(game,p)):null)).ToArray();
    private void CheckGuards(SwitchJournal journal) => VerifyGuards(journal);

    private SwitchJournal RefreshGuards(SwitchJournal journal)
    {
        var guards = new Guard[journal.Guards.Length];
        VerifyGuards(journal, guards);
        return journal with { Guards = guards };
    }

    private void VerifyGuards(SwitchJournal journal, Guard[]? verified = null)
    {
        for (var i = 0; i < journal.Guards.Length; i++)
        {
            var guard = journal.Guards[i];
            var path = SafePaths.Resolve(journal.Game, guard.Path);
            if (Directory.Exists(path))
                throw new InvalidOperationException("External change prevents profile recovery: " + guard.Path);
            var hash = File.Exists(path) ? Hash(path) : null;
            if (hash != guard.Hash && (!guard.AllowsNext || hash != guard.NextHash))
                throw new InvalidOperationException("External change prevents profile recovery: " + guard.Path);
            if (verified != null)
                verified[i] = new Guard(guard.Path, hash);
        }
    }

    public ProfilePlan PreviewReplacement(string winner, string gameFolder, ModLibrary library, CollectionStore collections)
    {
        var game = ModLibrary.ValidateGame(gameFolder, profile);
        var packages = library.List();
        var candidate = packages.Single(package => package.Id == winner);
        var active = library.GetActive();
        var report = ModConflictDetails.Explain(candidate, packages, active, game, 1000);
        if (report.Truncated) throw new InvalidOperationException("Too many conflicting files to review safely.");
        var losers = report.Files.Select(file => file.OwnerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = active.Where(item => Same(item.Game, game) && !losers.Contains(item.Id)).Select(item => item.Id)
            .Append(winner).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return PreviewSelection(new ModProfile(Guid.NewGuid().ToString("N"), candidate.Name, desired), game, library, collections);
    }

    public void ApplyReplacement(ProfilePlan plan, string gameFolder, ModLibrary library, CollectionStore collections)
        => ApplySelection(plan, gameFolder, library, collections, false);

    public void Apply(ProfilePlan plan,string gameFolder,ModLibrary library,CollectionStore collections)
    {
        ApplySelection(plan,gameFolder,library,collections,true);
    }
    internal void ApplySelection(ProfilePlan plan,string gameFolder,ModLibrary library,CollectionStore collections,bool trackApplied, IProgress<CollectionApplyProgress>? progress = null)
    {
        var fresh=trackApplied?Preview(plan.Profile.Id,gameFolder,library,collections):PreviewSelection(plan.Profile,gameFolder,library,collections);
        ApplyValidatedSelection(plan, fresh, gameFolder, library, collections, trackApplied, progress);
    }

    private void ApplyValidatedSelection(ProfilePlan plan, ProfilePlan fresh, string gameFolder, ModLibrary library, CollectionStore collections, bool trackApplied, IProgress<CollectionApplyProgress>? progress)
    {
        if(!plan.Profile.ModIds.ToHashSet().SetEquals(fresh.Profile.ModIds)||!Same(plan.Profile.Preset??"",fresh.Profile.Preset??"")||plan.Profile.Name!=fresh.Profile.Name||!plan.Enable.ToHashSet().SetEquals(fresh.Enable)||!plan.Disable.ToHashSet().SetEquals(fresh.Disable)||!plan.Keep.ToHashSet().SetEquals(fresh.Keep))throw new InvalidOperationException("Profile or game state changed; review a new preview.");
        var game=Folder(gameFolder);var ini=SafePaths.Resolve(game,"ReShade.ini");
        if(File.Exists(ini)&&new FileInfo(ini).Length>4*1024*1024)throw new InvalidOperationException("ReShade.ini exceeds 4 MiB.");
        var paths=library.GetActive().Where(a=>Same(a.Game,game)).SelectMany(a=>a.Files.Select(f=>f.Path)).Concat(library.List().Where(m=>fresh.Profile.ModIds.Contains(m.Id)).SelectMany(m=>m.Files.Select(f=>Target(m,f)))).Append("ReShade.ini").ToArray();
        var journal=new SwitchJournal(game,library.GetActive().Where(a=>Same(a.Game,game)).Select(a=>a.Id).ToArray(),fresh.Profile.ModIds,File.Exists(ini)?File.ReadAllBytes(ini):null,Snapshot(game,paths),AppliedProfiles().SingleOrDefault(a=>Same(a.Game,game)));Save(Pending,journal);
        try
        {
            var completed = 0;
            var total = fresh.Disable.Length + fresh.Enable.Length;
            void Report(string id) => progress?.Report(new(completed, total, id));
            foreach(var id in fresh.Disable){Report(id);CheckGuards(journal);journal=Intent(journal,library,id,false);Save(Pending,journal);library.Disable(id,game);journal=RefreshGuards(journal);Save(Pending,journal);completed++;}
            foreach(var id in fresh.Enable){Report(id);CheckGuards(journal);collections.EnsureCanEnable(id,library,game);journal=Intent(journal,library,id,true);Save(Pending,journal);library.Enable(id,game);journal=RefreshGuards(journal);Save(Pending,journal);completed++;}
            CheckGuards(journal);
            if(trackApplied)
            {
            journal=Intent(journal,library,null,false,fresh.Preset);Save(Pending,journal);
            if(fresh.Preset==null)ReShadePresets.ClearDefault(profile,game,SafePaths.Resolve(root,"reshade-backups"));else ReShadePresets.SetDefault(profile,game,fresh.Preset,SafePaths.Resolve(root,"reshade-backups"));
            journal=RefreshGuards(journal);Save(Pending,journal);
            }
            if(trackApplied)Save(CurrentFile,AppliedProfiles().Where(a=>!Same(a.Game,game)).Append(new Applied(game,fresh.Profile)).ToArray());File.Delete(Pending);
        }
        catch(Exception failure)
        {
            try{Recover(game,library,collections);}catch(Exception recovery){throw new InvalidOperationException("Profile switch failed; recovery remains pending. "+recovery.Message,failure);}throw;
        }
    }
    public void Recover(string gameFolder,ModLibrary library,CollectionStore collections)
    {
        if(!HasPending)return;var game=ModLibrary.ValidateGame(gameFolder,profile);var journal=Read<SwitchJournal>(Pending);if(!Same(journal.Game,game))throw new InvalidOperationException("Recover using the original game folder.");
        // Finish any single-package recovery first; that journal independently checks external edits.
        CheckGuards(journal);library.Recover(game);
        journal=RefreshGuards(journal);Save(Pending,journal);
        foreach(var id in Order(library.GetActive().Where(a=>Same(a.Game,game)).Select(a=>a.Id),collections,false).Reverse().Where(id=>!journal.Before.Contains(id)))
        {CheckGuards(journal);journal=Intent(journal,library,id,false);Save(Pending,journal);library.Disable(id,game);journal=RefreshGuards(journal);Save(Pending,journal);}
        foreach(var id in Order(journal.Before,collections,false).Where(id=>!library.GetActive().Any(a=>a.Id==id&&Same(a.Game,game))))
        {CheckGuards(journal);journal=Intent(journal,library,id,true);Save(Pending,journal);library.Enable(id,game);journal=RefreshGuards(journal);Save(Pending,journal);}
        CheckGuards(journal);journal=journal with{Guards=journal.Guards.Select(g=>Same(g.Path,"ReShade.ini")?g with{AllowsNext=true,NextHash=journal.Ini==null?null:Convert.ToHexString(SHA256.HashData(journal.Ini))}:g).ToArray()};Save(Pending,journal);var ini=SafePaths.Resolve(game,"ReShade.ini");if(journal.Ini==null){if(File.Exists(ini))File.Delete(ini);}else AtomicFiles.Write(ini,s=>s.Write(journal.Ini));Save(CurrentFile,AppliedProfiles().Where(a=>!Same(a.Game,game)).Concat(journal.BeforeApplied==null?Array.Empty<Applied>():new[]{journal.BeforeApplied}).ToArray());File.Delete(Pending);
    }
}
