using System.Text.RegularExpressions;
namespace Atlas.Core;
public sealed record SkinMappedFile(string EntryName,string TargetPath);
public sealed record AutoLinkSkinPlan(SkinMappedFile[] Files,string[] Characters,bool NeedsCharacter,bool CanChooseCharacter);
public static class AutoLinkCharacters
{
    public static IReadOnlyList<string> Names {get;}=Array.AsReadOnly("ZACK TINA JANNLEE EIN HAYABUSA KASUMI GENFU HELENA LEON BASS KOKORO HAYATE LEIFANG AYANE ELIOT LISA ALPHA152 BRAD CHRISTIE HITOMI RTM BAYMAN RIG MILA AKIRA SARAH PAI AYANE_BOSS HAYATE_BOSS KASUMI_BOSS HAYABUSA_BOSS MOMIJI RACHEL JACKY MARIE PHASE4 NYOTENGU HONOKA RAIDOU NAOTORA MAI".Split(' '));
    private static string Words(string value)=>Regex.Replace(value.ToUpperInvariant(),"[^A-Z0-9]+"," ").Trim();
    public static string? Suggest(string folder)
    {
        var text=" "+Words(folder)+" ";var matches=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in Names)if(text.Contains(" "+Words(name)+" ",StringComparison.Ordinal))matches.Add(name);
        foreach(var (alias,name) in new[]{("BRAD WONG","BRAD"),("MARIE ROSE","MARIE"),("JANN LEE","JANNLEE"),("GEN FU","GENFU"),("LEI FANG","LEIFANG"),("ALPHA 152","ALPHA152"),("PHASE 4","PHASE4"),("NYO TENGU","NYOTENGU")})if(text.Contains(" "+alias+" ",StringComparison.Ordinal))matches.Add(name);
        foreach(var boss in matches.Where(n=>n.EndsWith("_BOSS",StringComparison.Ordinal)).ToArray())matches.Remove(boss[..^5]);
        return matches.Count==1?matches.Single():null;
    }
    public static bool SkinPayload(string path)=>new[]{".tmc",".tmcl",".h",".hlp",".--p"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    private static bool Note(string path)=>new[]{".txt",".md",".pdf",".jpg",".jpeg",".png",".bmp",".url"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    public static AutoLinkSkinPlan Plan(IEnumerable<string> files,string? character=null)
    {
        if(character!=null&&!Names.Contains(character,StringComparer.Ordinal))throw new InvalidOperationException(L.T("CharacterChoose"));
        var input=files.Select(p=>(Entry:p,Relative:AutoLinkLayout.SkinFile(p))).ToArray();
        if(input.Any(p=>p.Relative.Contains("/AutoLink/",StringComparison.OrdinalIgnoreCase))||!input.Any(p=>Path.GetExtension(p.Relative).Equals(".tmc",StringComparison.OrdinalIgnoreCase))||input.Any(p=>!SkinPayload(p.Relative)&&!Note(p.Relative)))throw new InvalidOperationException(L.T("CharacterLayoutUnknown"));
        string Root(string path)=>path.Contains('/')?path.Split('/')[0]:"";
        var roots=input.Where(p=>SkinPayload(p.Relative)).Select(p=>Root(p.Relative)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var nestedCharacter=input.Where(p=>SkinPayload(p.Relative)).Any(p=>{var parts=p.Relative.Split('/');return parts.Length>2&&Suggest(parts[0])==null&&Suggest(parts[1])!=null;});
        var canChoose=roots.Length==1&&!nestedCharacter;
        if(character!=null&&!canChoose)throw new InvalidOperationException(L.T("CharacterSplitRoots"));
        var map=roots.ToDictionary(r=>r,r=>character??Suggest(r),StringComparer.OrdinalIgnoreCase);
        var known=map.Values.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(nestedCharacter||map.Values.Any(v=>v==null))return new([],known,true,canChoose);
        var mapped=input.Select(p=>{var root=Root(p.Relative);if(!map.TryGetValue(root,out var target))return new SkinMappedFile(p.Entry,p.Relative);return new SkinMappedFile(p.Entry,target+"/"+(root.Length==0?p.Relative:p.Relative[(root.Length+1)..]));}).ToArray();
        ImportAnalysis.Targets(mapped.Select(p=>p.TargetPath),"",false);
        return new(mapped,known,false,canChoose);
    }
    public static string[] ForPackage(ModPackage package)
    {
        if(package.Characters is {Length:>0})return package.Characters.Where(n=>Names.Contains(n,StringComparer.Ordinal)).Distinct().ToArray();
        return package.Files.Select(f=>package.Destination.Length==0?f:package.Destination+"/"+f).Where(p=>p.StartsWith("AutoLink/",StringComparison.OrdinalIgnoreCase)&&SkinPayload(p)).Select(p=>Suggest(p[9..].Split('/')[0])).OfType<string>().Distinct().ToArray();
    }
}
