using System.Text.Json;

namespace Atlas.Core;

public sealed record NexusUpdateCheck(NexusFile? Pinned, NexusFile? Update, bool ReviewNeeded);

public sealed partial class NexusApiClient
{
    public async Task<NexusUpdateCheck> CheckUpdateAsync(NexusLink link, CancellationToken cancellationToken)
    {
        link.Validate();
        using var document = await Get($"games/{link.GameDomain}/mods/{link.ModId}/files.json", cancellationToken);
        var root = document.RootElement;
        var files = new Dictionary<long, NexusFile>();
        foreach (var file in root.GetProperty("files").EnumerateArray())
        {
            var id = file.GetProperty("file_id").GetInt64();
            files[id] = new(id, file.GetProperty("name").GetString() ?? "", file.GetProperty("file_name").GetString() ?? "",
                file.GetProperty("version").GetString() ?? "", file.TryGetProperty("size_in_bytes", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0);
        }

        var pinned = files.GetValueOrDefault(link.FileId);
        if (!root.TryGetProperty("file_updates", out var updates))
            return new(pinned, null, pinned == null);

        var edges = new Dictionary<long, HashSet<long>>();
        foreach (var update in updates.EnumerateArray())
        {
            var oldId = update.GetProperty("old_file_id").GetInt64();
            var newId = update.GetProperty("new_file_id").GetInt64();
            if (!edges.TryGetValue(oldId, out var successors)) edges[oldId] = successors = [];
            successors.Add(newId);
        }

        var current = link.FileId;
        var visited = new HashSet<long> { current };
        while (edges.TryGetValue(current, out var successors))
        {
            if (successors.Count != 1) return new(pinned, null, true);
            current = successors.Single();
            if (!visited.Add(current)) return new(pinned, null, true);
        }

        if (current == link.FileId) return new(pinned, null, pinned == null);
        var candidate = files.GetValueOrDefault(current);
        return candidate != null && ModArchives.IsSupported(candidate.FileName)
            ? new(pinned, candidate, false)
            : new(pinned, null, true);
    }

    public Task<byte[]?> GetModCoverAsync(NexusLink link, CancellationToken cancellationToken) =>
        GetModCoverAsync(link.GameDomain, link.ModId, cancellationToken);

    public async Task<byte[]?> GetModCoverAsync(string domain, long modId, CancellationToken cancellationToken)
    {
        if (modId <= 0) throw new ArgumentOutOfRangeException(nameof(modId));
        var gameDomain = NexusGames.Domain(NexusGames.GameId(domain));
        using var document = await Get($"games/{gameDomain}/mods/{modId}.json", cancellationToken);
        if (!document.RootElement.TryGetProperty("picture_url", out var picture) ||
            picture.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(picture.GetString(), UriKind.Absolute, out var url) ||
            url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Host is not ("staticdelivery.nexusmods.com" or "images.nexusmods.com")) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("image/png, image/jpeg");
        using var response = await downloads.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        const int limit = 8 * 1024 * 1024;
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > limit ||
            response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp")) return null;
        using var output = new MemoryStream();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > limit) return null;
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
