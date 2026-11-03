namespace Atlas.Core;

internal static class SafePaths
{
    public static string Relative(string path, bool allowEmpty = false)
    {
        path = path.Replace('\\', '/');
        if (allowEmpty && path.Length == 0) return "";
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new InvalidOperationException(L.T("InvalidRelativePath"));
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" }.Contains(stem) ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                throw new InvalidOperationException(L.T("UnsafePath", path));
        }
        return path;
    }
    public static string Resolve(string root, string relative)
    {
        relative = Relative(relative);
        root = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("PathOutside"));
        NoLinks(result);
        return result;
    }
    public static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(L.T("LinksNotAllowed", current));
    }
}
