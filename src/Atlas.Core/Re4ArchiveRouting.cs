using System.Text.RegularExpressions;

namespace Atlas.Core;

internal static class Re4ArchiveRouting
{
    internal const string Autorun = "reframework/autorun";

    internal static bool IsAutorun(ModArchives.Entry entry)
    {
        if (!entry.FullName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || entry.Length > 262144) return false;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var buffer = new char[262144];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        var script = new string(buffer, 0, count);
        return Regex.IsMatch(script, @"\b(?:re\s*\.\s*on_\w+|sdk\s*\.\s*hook)\s*\(");
    }

    internal static Dictionary<string, string>? Map(IReadOnlyList<ModArchives.Entry> entries)
    {
        var looseScripts = entries.Where(entry => entry.FullName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (looseScripts is [var lua] && !lua.FullName.Contains('/') && IsAutorun(lua) && entries.All(entry => entry == lua || IsAncillary(entry.FullName)))
            return new(StringComparer.Ordinal) { [lua.FullName] = Autorun + "/" + lua.FullName };
        var payload = entries.Select(entry =>
        {
            var offset = entry.FullName.IndexOf("/natives/", StringComparison.OrdinalIgnoreCase);
            if (offset < 0) offset = entry.FullName.IndexOf("/reframework/", StringComparison.OrdinalIgnoreCase);
            return (entry.FullName, Root: offset < 0 ? "" : entry.FullName[..offset], Target: offset < 0 ? "" : entry.FullName[(offset + 1)..]);
        }).Where(item => item.Root.Length > 0).ToArray();
        if (payload.Length == 0 || entries.Any(entry => (entry.FullName.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) || entry.FullName.StartsWith("reframework/", StringComparison.OrdinalIgnoreCase)))) return null;
        var roots = payload.Select(item => item.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 1)
        {
            if (entries.Any(entry => !payload.Any(item => item.FullName == entry.FullName) &&
                !IsAncillary(entry.FullName))) return null;
            return payload.ToDictionary(item => item.FullName, item => item.Target, StringComparer.Ordinal);
        }
        var main = roots.Where(root => Regex.IsMatch(Path.GetFileName(root), @"^0\)\s*-\s*Main files$", RegexOptions.IgnoreCase)).ToArray();
        var primary = roots.Where(root => Regex.IsMatch(Path.GetFileName(root), @"^1\)\s*-\s*.+$")).ToArray();
        var optional = roots.Where(root => Regex.IsMatch(Path.GetFileName(root), @"^3\)\s*-\s*.+$")).ToArray();
        if (main.Length != 1 || primary.Length != 1 || optional.Length == 0 || roots.Length != optional.Length + 2) return null;
        if (entries.Any(entry => !payload.Any(item => item.FullName == entry.FullName) &&
            !IsAncillary(entry.FullName))) return null;
        // Numbered Fluffy packs can separate required shared files from the primary outfit and optional add-ons.
        var selected = payload.Where(item => item.Root == main[0]).Concat(payload.Where(item => item.Root == primary[0]))
            .GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase).Select(group => group.Last());
        return selected.ToDictionary(item => item.FullName, item => item.Target, StringComparer.Ordinal);
    }

    private static bool IsAncillary(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".ini" or ".txt" or ".md" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".pdf";
}
