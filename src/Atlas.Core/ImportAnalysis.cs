namespace Atlas.Core;

public enum ArchiveImportKind { Unknown, AutoLinkSkin, DirectFiles, ReShade }
public sealed record ArchiveImportAnalysis(ArchiveImportKind Kind,string[] Files,string Destination,string[] WarningKeys,ReShadeImportPlan? ReShade=null,AutoLinkSkinPlan? Skin=null);
public sealed record ImportTargetReview(bool FolderConfigured,string[] Conflicts,string[] ExistingFiles,string[] UnsafePaths);

/// <summary>Conservative structural suggestions. Inspection never deploys or executes files.</summary>
public static class ImportAnalysis
{
    private static readonly HashSet<string> Notes=new([".txt",".md",".pdf",".jpg",".jpeg",".png",".bmp",".url"],StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Tools=new([".exe",".msi",".bat",".cmd",".ps1",".lnk",".jar"],StringComparer.OrdinalIgnoreCase);
    public static ArchiveImportAnalysis Inspect(string archivePath,GameProfile profile)
    {
        string[] files;using(var archive=ModArchives.Open(archivePath)){if(profile.Id=="re4"&&ModLibrary.VariantTargets(archive) is {} targets)return new(ArchiveImportKind.DirectFiles,targets,"",["SmartModVariants"]);if(profile.Id=="re4"&&Re4ArchiveRouting.Map(archive.Files) is {} mapping)return new(ArchiveImportKind.DirectFiles,mapping.Values.ToArray(),"",["SmartVerifyInstructions"]);var artwork=ArchiveArtwork.Find(archive.Files);files=archive.Files.Where(f=>!ArchiveArtwork.Metadata(f.FullName,artwork)).Select(f=>f.FullName).ToArray();}
        if(files.Length==0)throw new InvalidOperationException(L.T("ZipEmpty"));
        var warnings=new List<string>();
        var tools=files.Any(p=>Tools.Contains(Path.GetExtension(p)));
        if(tools)warnings.Add("SmartExternalTool");
        var reShade=ReShadePackage.Inspect(archivePath);
        if(reShade!=null){warnings.Add("SmartVerifyInstructions");return new(ArchiveImportKind.ReShade,files,"",warnings.ToArray(),reShade);}
        var payload=files.Where(p=>!Notes.Contains(Path.GetExtension(p))).ToArray();
        var dll=payload.Any(p=>Path.GetExtension(p).Equals(".dll",StringComparison.OrdinalIgnoreCase));
        var wrapped=files.Any(p=>p.Contains("/AutoLink/",StringComparison.OrdinalIgnoreCase));
        if(profile.Id=="doa5"&&!tools&&!dll&&!wrapped&&payload.Length>0)
        {
            var skins=payload.Any(p=>Path.GetExtension(p).Equals(".tmc",StringComparison.OrdinalIgnoreCase))&&payload.All(AutoLinkCharacters.SkinPayload);
            if(skins){var skinPlan=AutoLinkCharacters.Plan(files);return new(ArchiveImportKind.AutoLinkSkin,files,"AutoLink",["SmartRequiresAutoLink"],null,skinPlan);}
            if(payload.All(p=>p.StartsWith("AutoLink/",StringComparison.OrdinalIgnoreCase)))return new(ArchiveImportKind.DirectFiles,files,"",["SmartVerifyInstructions"]);
        }
        if (profile.Id == "re4" && !tools)
        {
            if (files.Length == 1 && files[0].Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase))
                return new(ArchiveImportKind.DirectFiles, files, "", ["SmartVerifyInstructions"]);
            if (payload.Length > 0 && payload.All(path => path.StartsWith("reframework/autorun/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("reframework/plugins/", StringComparison.OrdinalIgnoreCase)))
                return new(ArchiveImportKind.DirectFiles, files, "", ["SmartRequiresReFramework"]);
        }
        warnings.Add("SmartUnknownHelp");
        if(dll)warnings.Add("SmartDllHelp");
        return new(ArchiveImportKind.Unknown,files,"",warnings.ToArray());
    }
    public static string[] Targets(IEnumerable<string> files,string destination,bool autoLinkSkin,string? character=null,bool canonicalCharacters=false)
    {
        if(autoLinkSkin&&canonicalCharacters){var plan=AutoLinkCharacters.Plan(files,character);if(plan.NeedsCharacter)throw new InvalidOperationException(L.T(plan.CanChooseCharacter?"CharacterChoose":"CharacterSplitRoots"));return Targets(plan.Files.Select(f=>f.TargetPath),destination,false);}
        var prefix=SafePaths.Relative(destination.Trim().Replace('\\','/'),true);
        var paths=files.Select(file=>{var relative=autoLinkSkin?AutoLinkLayout.SkinFile(file):SafePaths.Relative(file);return SafePaths.Relative(prefix.Length==0?relative:prefix+"/"+relative);}).ToArray();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)if(!seen.Add(path))throw new InvalidOperationException(L.T("ZipDuplicate"));
        // A normalized file may not become the parent of another normalized target.
        foreach(var path in paths)for(var slash=path.LastIndexOf('/');slash>=0;slash=path.LastIndexOf('/',slash-1))if(seen.Contains(path[..slash]))throw new InvalidOperationException(L.T("ZipDuplicate"));
        return paths;
    }
    public static ImportTargetReview Review(string[] targets,string gameFolder,IReadOnlyList<ActiveMod> active,IReadOnlyList<ModPackage>? packages=null)
    {
        targets=Targets(targets,"",false);
        if(string.IsNullOrWhiteSpace(gameFolder)||!Directory.Exists(gameFolder))return new(false,[],[],[]);
        gameFolder=Path.GetFullPath(gameFolder);
        var candidate=new ModPackage("__import_preview__","Import preview","","",targets);
        var status=ModVisualStatus.Evaluate((packages??[]).Where(p=>p.Id!=candidate.Id).Append(candidate).ToArray(),active,[],gameFolder)[candidate.Id];
        try{SafePaths.NoLinks(gameFolder);}catch(Exception error)when(error is InvalidOperationException or IOException or UnauthorizedAccessException){return new(true,status.Conflicts,[],[gameFolder]);}
        var existing=new List<string>();var unsafePaths=new List<string>();
        foreach(var path in targets)
        {
            try
            {
            var full=SafePaths.Resolve(gameFolder,path);
            if(File.Exists(full)||Directory.Exists(full)){existing.Add(path);continue;}
            var relative=path;while(relative.Contains('/')){relative=relative[..relative.LastIndexOf('/')];if(File.Exists(SafePaths.Resolve(gameFolder,relative))){existing.Add(path);break;}}
            }catch(Exception error)when(error is InvalidOperationException or IOException or UnauthorizedAccessException){unsafePaths.Add(path);}
        }
        return new(true,status.Conflicts,existing.ToArray(),unsafePaths.ToArray());
    }
}
