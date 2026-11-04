namespace Atlas.Core;

public sealed partial class ModLibrary
{
    private static bool IsDmc5Message(string path) =>
        path.StartsWith("natives/x64/message/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".msg.14", StringComparison.OrdinalIgnoreCase);

    public bool CanKeepActiveMessages(ModPackage mod, ModConflictReport report) =>
        profile.Id == "dmc5" && mod.Destination.Length == 0 && !report.Truncated && report.Files.Length > 0 &&
        report.Files.All(file => IsDmc5Message(file.CandidatePath) && Same(file.CandidatePath, file.ExistingPath)) &&
        mod.Files.Any(file => !IsDmc5Message(file));

    public void KeepActiveMessages(string id, string game)
    {
        game = ValidateGame(game, profile);
        NoPending();
        var active = Active();
        if (active.Any(mod => Same(mod.Id, id))) throw new InvalidOperationException(L.T("DisableBeforeDelete"));
        var packages = List();
        var mod = packages.Single(package => Same(package.Id, id));
        var report = ModConflictDetails.Explain(mod, packages, active, game, 1000);
        if (!CanKeepActiveMessages(mod, report)) throw new InvalidOperationException(L.T("ModBatchProblems"));
        ExcludeMessages(id, report.Files.Select(file => file.CandidatePath));
    }

    public string[] SharedMessageOverrides(ModPackage winner, ModPackage loser)
    {
        if (profile.Id != "dmc5" || winner.Destination.Length != 0 || loser.Destination.Length != 0 ||
            !loser.Files.Any(file => !IsDmc5Message(file))) return [];
        var shared = winner.Files.Intersect(loser.Files, StringComparer.OrdinalIgnoreCase).ToArray();
        return shared.All(IsDmc5Message) ? shared : [];
    }

    public void PreferCollectionMessages(string winnerId, string loserId)
    {
        NoPending();
        if (Active().Any(mod => Same(mod.Id, loserId))) throw new InvalidOperationException(L.T("ModBatchProblems"));
        var packages = List();
        var shared = SharedMessageOverrides(packages.Single(mod => Same(mod.Id, winnerId)),
            packages.Single(mod => Same(mod.Id, loserId)));
        if (shared.Length == 0) throw new InvalidOperationException(L.T("ModBatchProblems"));
        ExcludeMessages(loserId, shared);
    }

    private void ExcludeMessages(string id, IEnumerable<string> paths)
    {
        var manifest = SafePaths.Resolve(Package(id), "mod.json");
        var original = Read<ModPackage>(manifest);
        var excluded = (original.ExcludedMessages ?? []).Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Save(manifest, original with { ExcludedMessages = excluded });
    }
    public void RestorePackageMessages(string id)
    {
        NoPending();
        if (Active().Any(mod => Same(mod.Id, id))) throw new InvalidOperationException(L.T("DisableBeforeDelete"));
        var manifest = SafePaths.Resolve(Package(id), "mod.json");
        var original = Read<ModPackage>(manifest);
        Save(manifest, original with { ExcludedMessages = null });
    }
}
