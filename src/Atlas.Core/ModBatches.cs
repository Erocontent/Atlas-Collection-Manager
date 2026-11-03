namespace Atlas.Core;

public enum ModBatchAction { Enable, Disable, Delete }
public sealed record ModBatchPlan(ModBatchAction Action, string[] Selected, string[] Enable, string[] Disable, string[] Delete, string[] Problems);
public sealed record ModBatchResult(string[] Completed, string[] Remaining, string? Error);

public sealed class ModBatchService(string libraryRoot, GameProfile profile)
{
    private sealed record Review(ModBatchPlan Snapshot, ProfilePlan? Profile, string State, string Game);
    private readonly Dictionary<ModBatchPlan, Review> reviews = new(ReferenceEqualityComparer.Instance);
    private static bool Same(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static string Folder(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string State(ModLibrary library)=>System.Text.Json.JsonSerializer.Serialize(library.GetActive());
    private static ModBatchPlan Copy(ModBatchPlan p)=>new(p.Action,p.Selected.ToArray(),p.Enable.ToArray(),p.Disable.ToArray(),p.Delete.ToArray(),p.Problems.ToArray());
    private static bool Equal(ModBatchPlan a,ModBatchPlan b)=>a.Action==b.Action&&a.Selected.SequenceEqual(b.Selected)&&a.Enable.SequenceEqual(b.Enable)&&a.Disable.SequenceEqual(b.Disable)&&a.Delete.SequenceEqual(b.Delete)&&a.Problems.SequenceEqual(b.Problems);
    public ModBatchPlan Preview(ModBatchAction action,string[] ids,string gameFolder,ModLibrary library,CollectionStore collections)
    {
        var selected=ids?.ToArray()??[];
        try
        {
            if(!Enum.IsDefined(action)||selected.Length==0||selected.Length>1000||selected.Any(id=>!Guid.TryParseExact(id,"N",out _))||selected.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selected.Length)throw new InvalidOperationException("Invalid batch selection.");
            var store=new ModProfileStore(libraryRoot,profile);if(store.HasPending)throw new InvalidOperationException("Recover the pending profile switch first.");library.EnsureReady();
            var packages=library.List();if(selected.Any(id=>!packages.Any(p=>Same(p.Id,id))))throw new InvalidOperationException("A selected package is missing.");
            var active=library.GetActive();ProfilePlan? selection=null;string game="";
            if(action!=ModBatchAction.Delete||active.Any(a=>selected.Contains(a.Id,StringComparer.OrdinalIgnoreCase)))
            {
                game=ModLibrary.ValidateGame(gameFolder,profile);
                if(action==ModBatchAction.Delete&&active.Any(a=>selected.Contains(a.Id,StringComparer.OrdinalIgnoreCase)&&!Same(a.Game,game)))throw new InvalidOperationException("A selected package is active in another game folder.");
                var current=active.Where(a=>Same(a.Game,game)).Select(a=>a.Id);
                var final=(action==ModBatchAction.Enable?current.Concat(selected):current.Except(selected,StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var preset=ReShadePresets.ReadDefault(game)?.Trim().Trim('"');if(preset!=null){if(preset.StartsWith("./")||preset.StartsWith(".\\"))preset=preset[2..];if(Path.IsPathRooted(preset))preset=Path.GetRelativePath(game,preset);preset=preset.Replace('\\','/');}
                if(preset!=null&&!store.AvailablePresets(final,library).Contains(preset,StringComparer.OrdinalIgnoreCase))preset=null;
                selection=store.PreviewSelection(new ModProfile("00000000000000000000000000000001","Batch",final,preset),game,library,collections);
            }
            var plan=new ModBatchPlan(action,selected,selection?.Enable??[],selection?.Disable??[],action==ModBatchAction.Delete?selected.ToArray():[],[]);
            reviews[plan]=new(Copy(plan),selection,State(library),game);return plan;
        }
        catch(Exception e) when(e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {return new(action,selected,[],[],[],[Problem(e)]);}
    }
    private static string Problem(Exception e)=>e is UnauthorizedAccessException?"Access to a required file was denied.":e is IOException?"A required file could not be read or changed.":e.Message;
    public ModBatchResult Apply(ModBatchPlan plan,string gameFolder,ModLibrary library,CollectionStore collections)
    {
        var completed=new List<string>();
        try
        {
            if(plan.Problems.Length>0||!reviews.TryGetValue(plan,out var review)||!Equal(plan,review.Snapshot)||review.State!=State(library))throw new InvalidOperationException("Batch or game state changed; review a new preview.");
            if(review.Game.Length>0&&!Same(review.Game,Folder(gameFolder)))throw new InvalidOperationException("Game folder changed; review a new preview.");
            var fresh=Preview(plan.Action,plan.Selected,gameFolder,library,collections);
            if(fresh.Problems.Length>0||!Equal(plan,fresh))throw new InvalidOperationException(fresh.Problems.FirstOrDefault()??"Batch changed; review a new preview.");
            var store=new ModProfileStore(libraryRoot,profile);
            if(review.Profile!=null)store.ApplySelection(review.Profile,gameFolder,library,collections,false);
            if(plan.Action==ModBatchAction.Delete)
                foreach(var id in plan.Delete){if(store.HasPending)throw new InvalidOperationException("Recover the pending profile switch first.");library.EnsureReady();library.Delete(id);completed.Add(id);}
            else completed.AddRange(plan.Selected);
            reviews.Remove(plan);return new(completed.ToArray(),[],null);
        }
        catch(Exception e) when(e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {return new(completed.ToArray(),plan.Selected.Except(completed).ToArray(),"Batch could not finish. Review the remaining items and recover any pending operation before retrying.");}
    }
}
