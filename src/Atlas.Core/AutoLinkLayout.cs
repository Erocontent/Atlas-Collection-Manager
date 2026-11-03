namespace Atlas.Core;

public static class AutoLinkLayout
{
    public static string SkinFile(string path)
    {
        path = SafePaths.Relative(path);
        return path.StartsWith("AutoLink/", StringComparison.OrdinalIgnoreCase)
            ? SafePaths.Relative(path[9..]) : path;
    }
}
