using System.Text.Json;
using System.Text.RegularExpressions;
namespace Atlas.Core;

public sealed class PlayerPreferences
{
    public string LauncherSkin { get; set; } = "command-center";
    public Dictionary<string, LauncherPalette> SkinPalettes { get; set; } = LauncherSkins.Ids.ToDictionary(id => id, LauncherSkins.Defaults);
    public string Language { get; set; } = "en";
    public string Accent { get; set; } = "#A78BFA";
    public int CoverWidth { get; set; } = 190;
    public int ModCardWidth { get; set; } = 190;
    public string ModLibraryView { get; set; } = "grid";
    public string Background { get; set; } = "";
    public Dictionary<string, string> Covers { get; set; } = [];
    public Dictionary<string, string> GamePaths { get; set; } = [];
}

public sealed class PlayerPreferencesStore(string root)
{
    public PlayerPreferences Load()
    {
        PlayerPreferences p;
        try { p = JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(Path.Combine(root, "preferences.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { p = new(); }
        p.GamePaths ??= []; p.Covers ??= [];
        if (!p.GamePaths.ContainsKey("doa5") && File.Exists(Path.Combine(root,"game-path.txt")))
            p.GamePaths["doa5"] = File.ReadAllText(Path.Combine(root,"game-path.txt"));
        Normalize(p);
        return p;
    }
    private static void Normalize(PlayerPreferences p)
    {
        LauncherSkins.Normalize(p);
        p.Language = L.Normalize(p.Language);
        if (p.Accent == null || !Regex.IsMatch(p.Accent, "^#[0-9A-Fa-f]{6}$")) p.Accent = "#A78BFA";
        if (p.CoverWidth is not (160 or 190 or 220)) p.CoverWidth = 190;
        if (p.ModCardWidth is not (160 or 190 or 220)) p.ModCardWidth = 190;
        if (p.ModLibraryView is not ("grid" or "list" or "compact")) p.ModLibraryView = "grid";
        p.Background ??= "";
        p.GamePaths ??= []; p.Covers ??= [];
    }
    public void Save(PlayerPreferences preferences)
    {
        Normalize(preferences);
        Directory.CreateDirectory(root);
        AtomicFiles.Write(Path.Combine(root,"preferences.json"), stream => JsonSerializer.Serialize(stream,preferences,new JsonSerializerOptions { WriteIndented=true }));
    }
}
