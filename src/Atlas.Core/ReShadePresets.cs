using System.Security.Cryptography;
using System.Text;

namespace Atlas.Core;

public static class ReShadePresets
{
    private static bool Section(string line, out string name)
    {
        var value = line.Trim(); name = "";
        if (!value.StartsWith('[') || !value.EndsWith(']')) return false;
        name = value[1..^1]; return true;
    }
    private static string? Value(List<string> lines, string key)
    {
        var general = false;
        foreach (var line in lines)
        {
            if (Section(line, out var section)) { general = section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase); continue; }
            var index = line.IndexOf('=');
            if (general && index >= 0 && line[..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(index + 1)..].Trim();
        }
        return null;
    }
    private static void Put(List<string> lines, string key, string? value)
    {
        var general = false; var insertion = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Section(lines[i], out var section))
            {
                general = section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase);
                if (general) insertion = i + 1;
                continue;
            }
            var index = lines[i].IndexOf('=');
            if (general && index >= 0 && lines[i][..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines.RemoveAt(i--); continue; }
            if (general) insertion = i + 1;
        }
        if (value == null) return;
        if (insertion < 0) { if (lines.Count > 0) lines.Add(""); lines.Add("[GENERAL]"); insertion = lines.Count; }
        lines.Insert(insertion, key + "=" + value);
    }
    private static List<string> Read(string folder)
    {
        var path = SafePaths.Resolve(folder, "ReShade.ini");
        return File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
    }
    public static string? ReadDefault(string folder)
    {
        var lines = Read(folder);
        var selected = Value(lines, "PresetPath");
        return string.IsNullOrWhiteSpace(selected) ? Value(lines, "StartupPresetPath") : selected;
    }
    private static string SelectedPath(string folder, string selected)
    {
        selected = selected.Trim().Trim('"');
        if (selected.StartsWith("./") || selected.StartsWith(".\\")) selected = selected[2..];
        if (!Path.IsPathRooted(selected)) return SafePaths.Resolve(folder, selected);
        var relative = Path.GetRelativePath(Path.GetFullPath(folder), selected);
        return SafePaths.Resolve(folder, relative);
    }
    private static void Save(string folder, string backupRoot, List<string> lines)
    {
        var path = SafePaths.Resolve(folder, "ReShade.ini");
        SafePaths.NoLinks(backupRoot);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToUpperInvariant())));
        var backup = SafePaths.Resolve(backupRoot, key + "/ReShade.ini");
        if (File.Exists(path) && !File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            AtomicFiles.Copy(path, backup);
        }
        AtomicFiles.Write(path, stream => { using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true); foreach (var line in lines) writer.WriteLine(line); });
    }
    public static void SetDefault(GameProfile profile, string folder, string presetRelative, string backupRoot)
    {
        folder = ModLibrary.ValidateGame(folder, profile);
        presetRelative = SafePaths.Relative(presetRelative);
        if (!ReShadePackage.IsPresetFile(SafePaths.Resolve(folder, presetRelative))) throw new InvalidOperationException(L.T("ReShadeInvalidPreset"));
        var lines = Read(folder);
        Put(lines, "PresetPath", ".\\" + presetRelative.Replace('/', '\\'));
        Put(lines, "StartupPresetPath", ".\\" + presetRelative.Replace('/', '\\'));
        foreach (var (key, standard) in new[] { ("EffectSearchPaths", @".\reshade-shaders\Shaders\**"), ("TextureSearchPaths", @".\reshade-shaders\Textures\**") })
        {
            var existing = Value(lines, key) ?? "";
            var parts = existing.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!parts.Any(p => p.Replace('/', '\\').Equals(standard, StringComparison.OrdinalIgnoreCase))) parts.Add(standard);
            Put(lines, key, string.Join(',', parts));
        }
        Save(folder, backupRoot, lines);
    }
    public static void ClearDefault(GameProfile profile, string folder, string backupRoot)
    {
        folder = ModLibrary.ValidateGame(folder, profile);
        if (!File.Exists(SafePaths.Resolve(folder, "ReShade.ini"))) return;
        var lines = Read(folder); Put(lines, "PresetPath", null); Put(lines, "StartupPresetPath", null);
        Save(folder, backupRoot, lines);
    }
    public static bool ClearMissingDefault(GameProfile profile, string folder, string backupRoot)
    {
        folder = ModLibrary.ValidateGame(folder, profile);
        var lines = Read(folder); var changed = false;
        foreach (var key in new[] { "PresetPath", "StartupPresetPath" })
        {
            var selected = Value(lines, key);
            if (string.IsNullOrWhiteSpace(selected)) continue;
            bool exists;
            try { exists = File.Exists(SelectedPath(folder, selected)); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // Existing configurations may point outside the game; Atlas does not own those selections.
                continue;
            }
            if (!exists) { Put(lines, key, null); changed = true; }
        }
        if (changed) Save(folder, backupRoot, lines);
        return changed;
    }
    public static string[] InstalledPresets(string folder, IEnumerable<string> relativeFiles) => relativeFiles.Select(path => SafePaths.Relative(path)).Where(relative => ReShadePackage.IsPresetFile(SafePaths.Resolve(folder, relative))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool HasRuntime(string folder, GameProfile? profile = null)
    {
        profile ??= GameCatalog.All.FirstOrDefault(game => game.Executables.Any(name => File.Exists(Path.Combine(folder,name))));
        var executable = profile?.Executables.Select(name => SafePaths.Resolve(folder,name)).FirstOrDefault(File.Exists);
        return executable != null && ReShadePackage.Proxies.Any(name =>
        {
            var path=SafePaths.Resolve(folder,name);
            return ReShadePackage.IsRuntime(path) && ReShadePackage.RuntimeMatchesGame(path,executable);
        });
    }
    public static string[] ConflictingRuntimeFiles(string folder, IEnumerable<string> incomingRelativePaths) => incomingRelativePaths.Select(path => SafePaths.Relative(path)).Where(path => !path.Contains('/') && ReShadePackage.Proxies.Contains(path, StringComparer.OrdinalIgnoreCase) && File.Exists(SafePaths.Resolve(folder, path)) && !ReShadePackage.IsRuntime(SafePaths.Resolve(folder, path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
