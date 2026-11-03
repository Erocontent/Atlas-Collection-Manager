namespace Atlas.Core;

public sealed record ModFileConflict(string OwnerId,string OwnerName,string CandidatePath,string ExistingPath);
public sealed record ModConflictReport(ModFileConflict[] Files,bool Truncated);

/// <summary>Read-only path explanation against authoritative active records. Never touches the game.</summary>
public static class ModConflictDetails
{
    private sealed record Owner(string Id,string Path);
    private sealed class Node
    {
        public Dictionary<string,Node> Children=new(StringComparer.OrdinalIgnoreCase);
        public List<Owner> Files=[];
    }
    public static ModConflictReport Explain(ModPackage candidate,IReadOnlyList<ModPackage> packages,IReadOnlyList<ActiveMod> active,string gameFolder,int maximum=200)
    {
        if(maximum is <1 or >1000)throw new ArgumentOutOfRangeException(nameof(maximum));
        var destination=SafePaths.Relative(candidate.Destination,true);
        var paths=candidate.Files.Select(p=>SafePaths.Relative(destination.Length==0?p:destination+"/"+SafePaths.Relative(p))).ToArray();
        if(string.IsNullOrWhiteSpace(gameFolder))return new([],false);
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder));
        var names=packages.ToDictionary(p=>p.Id,p=>p.Name,StringComparer.OrdinalIgnoreCase);
        var root=new Node();
        foreach(var record in active)
        {
            if(!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(record.Game)),folder,StringComparison.OrdinalIgnoreCase))continue;
            foreach(var file in record.Files)
            {
                var path=SafePaths.Relative(file.Path);var node=root;
                foreach(var segment in path.Split('/')){if(!node.Children.TryGetValue(segment,out var child))node.Children.Add(segment,child=new());node=child;}
                node.Files.Add(new(record.Id,path));
            }
        }
        var found=new List<ModFileConflict>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Add(Node node,string path)
        {
            foreach(var owner in node.Files)
            {
                if(owner.Id.Equals(candidate.Id,StringComparison.OrdinalIgnoreCase)||!seen.Add(path+"\0"+owner.Id+"\0"+owner.Path))continue;
                found.Add(new(owner.Id,names.GetValueOrDefault(owner.Id,owner.Id),path,owner.Path));
                if(found.Count>maximum)return true;
            }
            return false;
        }
        ModConflictReport Result()=>new(found.Take(maximum).ToArray(),found.Count>maximum);
        foreach(var path in paths)
        {
            Node? node=root;
            foreach(var segment in path.Split('/'))
            {
                if(Add(node,path))return Result();
                if(!node.Children.TryGetValue(segment,out node))break;
            }
            if(node==null)continue;
            var pending=new Stack<Node>();pending.Push(node);
            while(pending.TryPop(out var next))
            {
                if(Add(next,path))return Result();
                foreach(var child in next.Children.Values)pending.Push(child);
            }
        }
        return Result();
    }
}
