using System.Text;
namespace Atlas.Core;
public sealed record ArchiveArtworkFile(string Name,byte[] Data);
public static class ArchiveArtwork
{
    private sealed record Source(string Name,long Length,Func<Stream> Open);
    private static bool Image(string path)=>new[]{".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    internal static string[] Find(IReadOnlyList<ModArchives.Entry> entries)=>Find(entries.Select(e=>new Source(e.FullName,e.Length,e.Open)).ToArray());
    internal static string[] FindStored(string content,IReadOnlyList<string> paths)=>Find(paths.Select(name=>{var file=SafePaths.Resolve(content,name);return new Source(name,File.Exists(file)?new FileInfo(file).Length:long.MaxValue,()=>File.OpenRead(file));}).ToArray());
    private static string[] Find(IReadOnlyList<Source> entries)
    {
        var paths=entries.Select(e=>e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);var chosen=new List<string>();
        foreach(var info in entries.Where(e=>Path.GetFileName(e.Name).Equals("modinfo.ini",StringComparison.OrdinalIgnoreCase)&&e.Length<=65536).Take(8))
        {
            using var stream=info.Open();using var bytes=new MemoryStream();var buffer=new byte[4096];int read;
            while((read=stream.Read(buffer))!=0){if(bytes.Length+read>65536)throw new InvalidOperationException("Preview metadata exceeds its limit.");bytes.Write(buffer,0,read);}
            var text=Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF');
            foreach(var line in text.Split('\n')){var at=line.IndexOf('=');if(at<0||!line[..at].Trim().Equals("screenshot",StringComparison.OrdinalIgnoreCase))continue;var name=line[(at+1)..].Trim().Trim('"','\'');if(name.Length==0||name.Contains('/')||name.Contains('\\')||name.Contains(':')||!Image(name))continue;var slash=info.Name.LastIndexOf('/');var path=(slash<0?"":info.Name[..(slash+1)])+name;if(paths.Contains(path))chosen.Add(path);}
        }
        chosen.AddRange(entries.Where(e=>e.Name.Count(c=>c=='/')<=1&&Image(e.Name)&&!new[]{"textures","shaders","natives","data"}.Contains(e.Name.Split('/')[0],StringComparer.OrdinalIgnoreCase)&&new[]{"screenshot","preview","cover","thumbnail"}.Contains(Path.GetFileNameWithoutExtension(e.Name),StringComparer.OrdinalIgnoreCase)).Select(e=>e.Name));
        return chosen.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
    }
    internal static bool Metadata(string path,IReadOnlyCollection<string> images)=>Path.GetFileName(path).Equals("modinfo.ini",StringComparison.OrdinalIgnoreCase)||images.Contains(path,StringComparer.OrdinalIgnoreCase);
}
