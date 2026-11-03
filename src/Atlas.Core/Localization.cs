using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Atlas.Core;

public sealed record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

public static class L
{
    public static IReadOnlyList<LanguageOption> Languages { get; } = new[]
    {
        new LanguageOption("en", "English"), new("pt-BR", "Português (Brasil)"),
        new("es", "Español"), new("fr", "Français"), new("de", "Deutsch"),
        new("it", "Italiano"), new("ru", "Русский"), new("ja", "日本語"),
        new("ko", "한국어"), new("zh-Hans", "简体中文")
    };
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new();
    public static string CurrentCode { get; private set; } = "en";
    public static string Normalize(string? code) => Languages.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    public static void SetLanguage(string? code) => CurrentCode = Normalize(code);
    public static IReadOnlyDictionary<string, string> Catalog(string code) => Catalogs.GetOrAdd(Normalize(code), language =>
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"Atlas.Core.Locales.{language}.json")
            ?? throw new InvalidOperationException("Missing language resource: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidOperationException("Invalid language resource: " + language);
    });
    public static string T(string key, params object?[] args)
    {
        var selected = Catalog(CurrentCode);
        var text = selected.TryGetValue(key, out var translated) ? translated : Catalog("en")[key];
        return args.Length == 0 ? text : string.Format(CultureInfo.GetCultureInfo(CurrentCode), text, args);
    }
}
