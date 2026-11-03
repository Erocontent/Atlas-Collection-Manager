using System.Globalization;
using System.Text.RegularExpressions;

namespace Atlas.Core;

public sealed record LauncherPalette(string Background, string Text, string Accent);

public static class LauncherSkins
{
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[] { "command-center", "cinematic" });
    public static string NormalizeId(string? id) => id == "cinematic" ? "cinematic" : "command-center";
    public static LauncherPalette Defaults(string? id) => NormalizeId(id) == "cinematic"
        ? new("#101013", "#FFFFFF", "#EF4740")
        : new("#11151B", "#FFFFFF", "#28D7E8");

    public static bool IsValid(LauncherPalette? palette) => palette is not null &&
        new[] { palette.Background, palette.Text, palette.Accent }.All(color => color is not null && Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"));

    public static bool IsReadable(LauncherPalette? palette) => IsValid(palette) &&
        Contrast(palette!.Text, palette.Background) >= 4.5 && Contrast(palette.Accent, palette.Background) >= 2.0;

    public static double Contrast(string first, string second)
    {
        static double Luminance(string color)
        {
            if (color is null || !Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("Use a six-digit RGB color.", nameof(color));
            double Linear(int offset)
            {
                var value = int.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(1) + 0.7152 * Linear(3) + 0.0722 * Linear(5);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static LauncherPalette Palette(PlayerPreferences preferences)
    {
        Normalize(preferences);
        return preferences.SkinPalettes[preferences.LauncherSkin];
    }

    internal static void Normalize(PlayerPreferences preferences)
    {
        preferences.LauncherSkin = NormalizeId(preferences.LauncherSkin);
        var existing = preferences.SkinPalettes;
        preferences.SkinPalettes = Ids.ToDictionary(id => id, id =>
            existing is not null && existing.TryGetValue(id, out var palette) && IsValid(palette) ? palette : Defaults(id));
    }
}
