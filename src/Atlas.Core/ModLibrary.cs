using System.Security.Cryptography;
using System.Text.Json;

namespace Atlas.Core;

public sealed class ModLibrary
{
    private readonly string root;
    private readonly GameProfile profile;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ModLibrary(string root, GameProfile? profile = null)
    {
        this.profile = profile ?? GameCatalog.Get("doa5");
        this.root = Path.GetFullPath(root);
        SafePaths.NoLinks(this.root);
        Directory.CreateDirectory(this.root);
        Directory.CreateDirectory(Path.Combine(this.root, "packages"));
    }
    private string Package(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException(L.T("InvalidId"));
        return SafePaths.Resolve(root, "packages/" + id);
    }
    private T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidOperationException(L.T("InvalidRecord"));
    private void Save<T>(string path, T value)
    {
        AtomicFiles.Write(path, stream => JsonSerializer.Serialize(stream, value, Json));
    }
    private string State => Path.Combine(root, "active.json");
    private string Pending => Path.Combine(root, "pending.json");
    private List<ActiveMod> Active() => File.Exists(State) ? Read<List<ActiveMod>>(State) : [];
    public IReadOnlyList<ActiveMod> GetActive() => Active();
    public IReadOnlyList<ModPackage> List() => Directory.EnumerateDirectories(Path.Combine(root, "packages"))
        .Where(p => File.Exists(Path.Combine(p, "mod.json"))).Select(p => Read<ModPackage>(Path.Combine(p, "mod.json"))).OrderBy(m => m.Name).ToList();

    public void Delete(string id)
    {
        NoPending();
        if (Active().Any(mod => mod.Id == id)) throw new InvalidOperationException(L.T("DisableBeforeDelete"));
        var folder = Package(id);
        if (!Directory.Exists(folder)) throw new InvalidOperationException(L.T("ModNotFound"));
        var directories = new Stack<string>(); directories.Push(folder);
        while (directories.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                SafePaths.NoLinks(entry);
                if (Directory.Exists(entry)) directories.Push(entry);
            }
        }
        // Package(id) validates containment and every descendant was checked for links.
        Directory.Delete(folder, true);
    }
    public ModPackage ImportZip(string zip, string name, string category, string destination, bool autoLinkSkin = false,string? character=null,bool canonicalCharacters=false)
        => ImportArchive(zip, name, category, destination, autoLinkSkin, null,character,canonicalCharacters);
    public ModPackage ImportReShade(string zip, string name, ReShadeImportPlan plan)
        => ImportArchive(zip, name, "ReShade", "", false, plan);
    private ModPackage ImportArchive(string zip, string name, string category, string destination, bool autoLinkSkin, ReShadeImportPlan? reShade,string? character=null,bool canonicalCharacters=false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(L.T("EnterModName"));
        if (autoLinkSkin && profile.Id != "doa5") throw new InvalidOperationException(L.T("AutoLinkOnlyDoa"));
        if (autoLinkSkin) destination = "AutoLink";
        destination = SafePaths.Relative(destination.Trim().Replace('\\','/'), true);
        var id = Guid.NewGuid().ToString("N");
        var folder = Package(id);
        var mapping = reShade?.Files.ToDictionary(file => file.EntryName.Replace('\\', '/'), file => SafePaths.Relative(file.TargetPath), StringComparer.Ordinal);
        if (mapping != null && mapping.Values.Any(path => Path.GetFileName(path).Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.T("ReShadePackageUnknown"));
        try
        {
            using var archive = ModArchives.Open(zip);
            var artwork=ArchiveArtwork.Find(archive.Files);
            var deployFiles=archive.Files.Where(e=>!ArchiveArtwork.Metadata(e.FullName,artwork)).ToArray();
            var skinPlan=autoLinkSkin&&canonicalCharacters?AutoLinkCharacters.Plan(deployFiles.Select(e=>e.FullName),character):null;
            if(skinPlan?.NeedsCharacter==true)throw new InvalidOperationException(L.T(skinPlan.CanChooseCharacter?"CharacterChoose":"CharacterSplitRoots"));
            var skinMapping=skinPlan?.Files.ToDictionary(f=>f.EntryName,f=>f.TargetPath,StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<(ModArchives.Entry entry, string path)>();
            foreach (var entry in deployFiles)
            {
                var path = entry.FullName;
                if (mapping != null)
                {
                    if (!mapping.TryGetValue(path, out var mapped)) continue;
                    path = mapped;
                }
                if (autoLinkSkin) path = skinMapping!=null?skinMapping[path]:AutoLinkLayout.SkinFile(path);
                if (!paths.Add(path)) throw new InvalidOperationException(L.T("ZipDuplicate"));
                entries.Add((entry, path));
            }
            if (entries.Count == 0) throw new InvalidOperationException(L.T("ZipEmpty"));
            Directory.CreateDirectory(Path.Combine(folder, "content"));
            long actualBytes = 0;
            foreach (var (entry, path) in entries)
            {
                var output = SafePaths.Resolve(Path.Combine(folder, "content"), path);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                entry.CopyTo(target, ref actualBytes);
            }
            for(var i=0;i<artwork.Length;i++){
                var entry=archive.Files.Single(e=>e.FullName.Equals(artwork[i],StringComparison.OrdinalIgnoreCase));if(entry.Length>8*1024*1024)continue;
                var directory=Path.Combine(folder,"artwork");Directory.CreateDirectory(directory);using var imageFile=new FileStream(SafePaths.Resolve(directory,i+Path.GetExtension(entry.FullName).ToLowerInvariant()),FileMode.CreateNew,FileAccess.Write);entry.CopyTo(imageFile,ref actualBytes,8*1024*1024);
            }
            var mod = new ModPackage(id, name.Trim(), category.Trim(), destination, entries.Select(e => e.path).ToArray(), 2, reShade == null ? "files" : "reshade", DateTimeOffset.UtcNow,skinPlan?.Characters);
            Save(Path.Combine(folder, "mod.json"), mod);
            return mod;
        }
        catch { if (Directory.Exists(folder)) Directory.Delete(folder, true); throw; }
    }
    public IReadOnlyList<ArchiveArtworkFile> ReadArtwork(string id)
    {
        var directory=SafePaths.Resolve(Package(id),"artwork");if(!Directory.Exists(directory)){
            var package=Read<ModPackage>(SafePaths.Resolve(Package(id),"mod.json"));var content=SafePaths.Resolve(Package(id),"content");
            return ArchiveArtwork.FindStored(content,package.Files).Select(name=>{var file=SafePaths.Resolve(content,name);if(!File.Exists(file)||new FileInfo(file).Length>8*1024*1024)return null;return new ArchiveArtworkFile(name,File.ReadAllBytes(file));}).OfType<ArchiveArtworkFile>().ToArray();
        }
        return Directory.EnumerateFiles(directory).Order().Take(4).Select(file=>{SafePaths.NoLinks(file);if(new FileInfo(file).Length>8*1024*1024)return null;return new ArchiveArtworkFile(Path.GetFileName(file),File.ReadAllBytes(file));}).OfType<ArchiveArtworkFile>().ToArray();
    }
    public static string ValidateGame(string game, GameProfile? profile = null)
    {
        profile ??= GameCatalog.Get("doa5");
        game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(game));
        SafePaths.NoLinks(game);
        if (!profile.Executables.Any(exe => File.Exists(Path.Combine(game, exe))))
            throw new InvalidOperationException(L.T("ChooseGameFolder", profile.Name, profile.Executables[0]));
        foreach (var exe in profile.Executables)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
            var running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException(L.T("CloseGame", profile.Name));
        }
        return game;
    }
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private string Backup(string id, string path) => SafePaths.Resolve(Path.Combine(Package(id), "originals"), path);
    private void NoPending() { if (File.Exists(Pending)) throw new InvalidOperationException(L.T("PendingOperation")); }
    public void EnsureReady() => NoPending();
    public string? ManagedAutoLinkProxy(string game)
    {
        if (profile.Id != "doa5") return null;
        var owner = Active().SingleOrDefault(mod => Same(mod.Game, game) && mod.Files.Any(file => Same(file.Path, "d3d9.dll")));
        if (owner == null) return null;
        var proxy = owner.Files.Single(file => Same(file.Path, "d3d9.dll"));
        if (proxy.HadOriginal) return null;
        var files = owner.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!files.Contains("dinput8.dll") || !files.Contains("d3d9_ui_mod.txt") || !files.Contains("AutoLink/NAME.txt")) return null;
        return owner.Id;
    }

    public void PrepareAutoLinkForReShade(string id, string game)
    {
        game = ValidateGame(game, profile);
        NoPending();
        if (ManagedAutoLinkProxy(game) != id) throw new InvalidOperationException(L.T("ReShadeProxyConflict", "d3d9.dll"));
        var manifest = Path.Combine(Package(id), "mod.json");
        var original = Read<ModPackage>(manifest);
        Disable(id, game);
        try
        {
            Save(manifest, original with { Files = original.Files.Where(file => !Same(file, "d3d9.dll")).ToArray() });
            Enable(id, game);
        }
        catch
        {
            if (!File.Exists(Pending) && !Active().Any(mod => mod.Id == id))
            {
                Save(manifest, original);
                Enable(id, game);
            }
            throw;
        }
    }

    public void Enable(string id, string game)
    {
        game = ValidateGame(game, profile); NoPending();
        var active = Active();
        if (active.Any(m => m.Id == id)) throw new InvalidOperationException(L.T("AlreadyActive"));
        var mod = Read<ModPackage>(Path.Combine(Package(id), "mod.json"));
        if (mod.Kind == "reshade")
        {
            var conflicts = ReShadePresets.ConflictingRuntimeFiles(game, mod.Files);
            if (conflicts.Length > 0) throw new InvalidOperationException(L.T("ReShadeProxyConflict", string.Join(", ", conflicts)));
            foreach(var file in mod.Files.Where(file=>ReShadePackage.Proxies.Contains(file,StringComparer.OrdinalIgnoreCase)))
            {
                var source=SafePaths.Resolve(Path.Combine(Package(id),"content"),file);
                if(!ReShadePackage.RuntimeMatchesGame(source,GameLauncher.Prepare(profile,game).FileName))
                    throw new InvalidOperationException(L.T("ReShadeArchitectureMismatch"));
            }
        }
        var content = Path.Combine(Package(id), "content");
        var files = new List<ManagedFile>();
        foreach (var file in mod.Files)
        {
            var relative = SafePaths.Relative(mod.Destination.Length == 0 ? file : mod.Destination + "/" + file);
            var target = SafePaths.Resolve(game, relative);
            if (profile.Executables.Any(exe => Same(relative, exe))) throw new InvalidOperationException(L.T("ExecutableProtected"));
            if (active.Where(m => Same(m.Game, game)).Any(m => m.Files.Any(f => Same(f.Path, relative))))
                throw new InvalidOperationException(L.T("Conflict", relative));
            if (Directory.Exists(target)) throw new InvalidOperationException(L.T("DirectoryInsteadFile", relative));
            for (var parent = Path.GetDirectoryName(target); parent != null && !Same(parent, game); parent = Path.GetDirectoryName(parent))
                if (File.Exists(parent)) throw new InvalidOperationException(L.T("FileInsteadDirectory", parent));
            files.Add(new ManagedFile(relative, Hash(SafePaths.Resolve(content, file)), File.Exists(target)));
        }
        foreach (var file in files.Where(f => f.HadOriginal))
        {
            var backup = Backup(id, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            AtomicFiles.Copy(SafePaths.Resolve(game, file.Path), backup);
        }
        var ownedByActiveMods = active.Where(mod => Same(mod.Game, game))
            .SelectMany(mod => mod.CreatedDirectories ?? ParentDirectories(mod.Files.Where(f => !f.HadOriginal)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyAddedParents = mod.FormatVersion < 2
            ? ParentDirectories(files.Where(file => !file.HadOriginal)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdDirectories = ParentDirectories(files).Where(path =>
            !Directory.Exists(SafePaths.Resolve(game, path)) || ownedByActiveMods.Contains(path) || legacyAddedParents.Contains(path)).ToArray();
        var installed = new ActiveMod(id, game, files.ToArray(), createdDirectories);
        Save(Pending, new Journal(game, "enable", installed));
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                var target = SafePaths.Resolve(game, files[i].Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                AtomicFiles.Copy(SafePaths.Resolve(content, mod.Files[i]), target);
            }
            active.Add(installed); Save(State, active); File.Delete(Pending);
        }
        catch { Recover(game); throw; }
    }
    public void Disable(string id, string game)
    {
        game = ValidateGame(game, profile); NoPending();
        var mod = Active().SingleOrDefault(m => m.Id == id && Same(m.Game, game)) ?? throw new InvalidOperationException(L.T("NotActiveHere"));
        var package = Read<ModPackage>(Path.Combine(Package(id), "mod.json"));
        var updatedFiles = mod.Files.ToArray();
        var editedPresets = new List<(string GameFile, string LibraryFile)>();
        for (var i = 0; i < mod.Files.Length; i++)
        {
            var file = mod.Files[i];
            var path = SafePaths.Resolve(game, file.Path);
            if (!File.Exists(path)) throw new InvalidOperationException(L.T("ExternalChanged", file.Path));
            var currentHash = Hash(path);
            if (currentHash == file.Hash) continue;
            if (package.Kind != "reshade" || package.Destination.Length != 0 || !ReShadePackage.IsPresetFile(path))
                throw new InvalidOperationException(L.T("ExternalChanged", file.Path));
            updatedFiles[i] = file with { Hash = currentHash };
            editedPresets.Add((path, SafePaths.Resolve(Path.Combine(Package(id), "content"), file.Path)));
        }
        mod = mod with { Files = updatedFiles };
        CheckRestore(mod);
        // ReShade writes the player's adjustments back into preset INIs. Preserve them in the library before removal.
        foreach (var (gameFile, libraryFile) in editedPresets) AtomicFiles.Copy(gameFile, libraryFile);
        if (editedPresets.Count > 0)
        {
            var registry = Active(); registry[registry.FindIndex(item => item.Id == id)] = mod; Save(State, registry);
        }
        Save(Pending, new Journal(game, "disable", mod));
        Restore(mod);
    }
    private void CheckRestore(ActiveMod mod)
    {
        foreach (var file in mod.Files)
        {
            var path = SafePaths.Resolve(mod.Game, file.Path);
            var original = file.HadOriginal ? Backup(mod.Id, file.Path) : null;
            if (original != null && !File.Exists(original)) throw new InvalidOperationException(L.T("OriginalMissing", file.Path));
            if (Directory.Exists(path)) throw new InvalidOperationException(L.T("UnexpectedDirectory", file.Path));
            if (File.Exists(path))
            {
                var current = Hash(path);
                if (current != file.Hash && (original == null || current != Hash(original)))
                    throw new InvalidOperationException(L.T("RecoveryExternal", file.Path));
            }
        }
    }
    private void Restore(ActiveMod mod)
    {
        CheckRestore(mod);
        foreach (var file in mod.Files)
        {
            var target = SafePaths.Resolve(mod.Game, file.Path);
            if (file.HadOriginal)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                AtomicFiles.Copy(Backup(mod.Id, file.Path), target);
            }
            else if (File.Exists(target)) File.Delete(target);
        }
        CleanupDirectories(mod);
        ReShadePresets.ClearMissingDefault(profile, mod.Game, Path.Combine(root, "reshade-backups"));
        Save(State, Active().Where(m => m.Id != mod.Id).ToList());
        File.Delete(Pending);
    }
    private static string[] ParentDirectories(IEnumerable<ManagedFile> files)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = file.Path;
            while (path.LastIndexOf('/') is var slash && slash >= 0)
            {
                path = path[..slash];
                directories.Add(SafePaths.Relative(path));
            }
        }
        return directories.OrderByDescending(path => path.Count(c => c == '/')).ToArray();
    }
    private void CleanupDirectories(ActiveMod mod)
    {
        var directories = mod.CreatedDirectories ?? ParentDirectories(mod.Files.Where(file => !file.HadOriginal));
        foreach (var relative in directories.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path => path.Count(c => c == '/')))
        {
            if (Same(relative, "AutoLink")) continue;
            var path = SafePaths.Resolve(mod.Game, relative);
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any()) continue;
            try { Directory.Delete(path, false); }
            catch (IOException) when (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) { }
        }
    }
    public void Recover(string game)
    {
        if (!File.Exists(Pending)) return;
        game = ValidateGame(game, profile);
        var journal = Read<Journal>(Pending);
        if (!Same(journal.Game, game) || !Same(journal.Mod.Game, game)) throw new InvalidOperationException(L.T("RecoveryFolder", journal.Game));
        Restore(journal.Mod);
    }
}
