using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reflection.PortableExecutable;

namespace Atlas.Core;

public sealed record ReShadeMappedFile(string EntryName, string TargetPath);
public sealed record ReShadeImportPlan(ReShadeMappedFile[] Files, string[] Presets, string[] IgnoredFiles);

public static class ReShadePackage
{
    public static bool RuntimeMatchesGame(string runtimePath, string executablePath)
    {
        SafePaths.NoLinks(runtimePath); SafePaths.NoLinks(executablePath);
        try
        {
            using var runtimeFile=File.OpenRead(runtimePath);using var executableFile=File.OpenRead(executablePath);
            using var runtime=new PEReader(runtimeFile);using var executable=new PEReader(executableFile);
            return runtime.PEHeaders.PEHeader!=null && executable.PEHeaders.PEHeader!=null &&
                runtime.PEHeaders.CoffHeader.Machine==executable.PEHeaders.CoffHeader.Machine &&
                runtime.PEHeaders.CoffHeader.Machine is Machine.I386 or Machine.Amd64 or Machine.Arm64;
        }
        catch(Exception e) when(e is IOException or BadImageFormatException){return false;}
    }
    internal static readonly string[] Proxies = ["d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll", "opengl32.dll", "ddraw.dll"];
    internal static bool PresetText(string text) => Regex.IsMatch(text, @"(?im)^\s*(Techniques|TechniqueSorting)\s*=") || Regex.IsMatch(text, @"(?im)^\s*\[[^\]\r\n]+\.fx\]\s*$");
    public static bool IsPresetFile(string path)
    {
        SafePaths.NoLinks(path);
        return !Path.GetFileName(path).Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && new FileInfo(path).Length <= 4 * 1024 * 1024 && PresetText(File.ReadAllText(path));
    }
    internal static bool IsRuntime(string path)
    {
        SafePaths.NoLinks(path);
        if (!File.Exists(path)) return false;
        try { return FileVersionInfo.GetVersionInfo(path).ProductName == "ReShade"; }
        catch (ArgumentException) { return false; }
    }
    public static ReShadeImportPlan? Inspect(string zip)
    {
        SafePaths.NoLinks(zip);
        using var archive = ModArchives.Open(zip);
        var entries = archive.Files.Select(entry => (Entry: entry, Path: entry.FullName)).ToList();
        long inspectedBytes = 0;
        // Peel only common packaging wrappers, never semantic resource directories.
        while (entries.Count > 0 && entries.All(e => e.Path.Contains('/')))
        {
            var first = entries[0].Path.Split('/')[0];
            if (new[] { "reshade-shaders", "Shaders", "Textures", "Presets" }.Contains(first, StringComparer.OrdinalIgnoreCase) || !entries.All(e => e.Path.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase))) break;
            entries = entries.Select(e => (e.Entry, e.Path[(first.Length + 1)..])).ToList();
        }
        var files = new List<ReShadeMappedFile>(); var presets = new List<string>(); var ignored = new List<string>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateTarget=false;
        foreach (var (entry, path) in entries)
        {
            string? target = null;
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path);
            if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) && !name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase) && entry.Length <= 4 * 1024 * 1024)
            {
                using var contents = new MemoryStream();
                entry.CopyTo(contents, ref inspectedBytes, 4 * 1024 * 1024);
                contents.Position = 0;
                using var reader = new StreamReader(contents);
                if (PresetText(reader.ReadToEnd())) { target = path; presets.Add(path); }
            }
            else if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) && Proxies.Contains(name, StringComparer.OrdinalIgnoreCase) && entry.Length <= 64L * 1024 * 1024)
            {
                var temporary = Path.Combine(Path.GetTempPath(), "atlas-reshade-" + Guid.NewGuid().ToString("N") + ".dll");
                try { using (var output = new FileStream(temporary, FileMode.CreateNew)) entry.CopyTo(output, ref inspectedBytes, 64L * 1024 * 1024); if (IsRuntime(temporary)) target = name; }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path.Split('/');
                var offset = Array.FindIndex(parts, part => part.Equals("Shaders", StringComparison.OrdinalIgnoreCase) || part.Equals("Textures", StringComparison.OrdinalIgnoreCase));
                if (offset >= 0 && parts.Length > offset + 1)
                    target = "reshade-shaders/" + (parts[offset].Equals("Shaders", StringComparison.OrdinalIgnoreCase) ? "Shaders/" : "Textures/") + string.Join('/', parts.Skip(offset + 1));
            }
            if (target == null) { ignored.Add(entry.FullName); continue; }
            if (!targets.Add(target)) duplicateTarget=true;
            files.Add(new(entry.FullName, SafePaths.Relative(target)));
        }
        var explicitShaderDirectory=entries.Any(entry=>entry.Path.Split('/').Any(part=>part.Equals("reshade-shaders",StringComparison.OrdinalIgnoreCase)));
        var verifiedRuntime=files.Any(file=>Proxies.Contains(file.TargetPath,StringComparer.OrdinalIgnoreCase));
        if(files.Count==0 || (presets.Count==0 && !explicitShaderDirectory && !verifiedRuntime))return null;
        if(duplicateTarget)throw new InvalidOperationException(L.T("ZipDuplicate"));
        return new(files.ToArray(), presets.ToArray(), ignored.ToArray());
    }
}
