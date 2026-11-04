using System.IO;
using System.Text.Json;
using Atlas.Core;

namespace Atlas.App;

public partial class PremiumWindow
{
    private static bool VersionUnknown(string version) =>
        string.IsNullOrWhiteSpace(version) || version.Equals("unverified", StringComparison.OrdinalIgnoreCase);

    private string ExactNexusFilePath(GameProfile game, NexusCollectionSource source) =>
        Path.Combine(data, "nexus-file-metadata", NexusGames.Domain(game.Id), $"{source.ModId}-{source.FileId}.json");

    private NexusFile? ExactNexusFile(GameProfile game, NexusCollectionSource source)
    {
        LoadNexusHistory();
        var fromDownload = nexusJobs.LastOrDefault(job => job.Link.GameDomain == NexusGames.Domain(game.Id) &&
            job.Link.ModId == source.ModId && job.Link.FileId == source.FileId && job.Download != null)?.Download?.File;
        if (fromDownload != null && !VersionUnknown(fromDownload.Version)) return fromDownload;
        try
        {
            var path = ExactNexusFilePath(game, source);
            if (File.Exists(path) && new FileInfo(path).Length <= 65536 &&
                JsonSerializer.Deserialize<NexusFile>(File.ReadAllText(path)) is {} cached && cached.FileId == source.FileId) return cached;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return fromDownload;
    }

    private void SaveExactNexusFile(GameProfile game, NexusCollectionSource source, NexusFile file)
    {
        var path = ExactNexusFilePath(game, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file));
    }

    private ModDetails WithNexusVersion(ModDetails details)
    {
        if (game != null && VersionUnknown(details.Version) && libraryNexusIds.TryGetValue(details.Id, out var source) &&
            ExactNexusFile(game, source) is {} file && !VersionUnknown(file.Version)) return details with { Version = file.Version };
        return details;
    }

    private string? NexusPackageLabel(string packageId)
    {
        if (game == null || !libraryNexusIds.TryGetValue(packageId, out var source)) return null;
        var file = ExactNexusFile(game, source);
        return string.IsNullOrWhiteSpace(file?.Name) ? $"Nexus File ID {source.FileId}" : $"{file.Name} · #{source.FileId}";
    }
}
