using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Atlas.Core;

/// <summary>Downloads the standard official setup and prepares its interactive GUI. Never launches it.</summary>
public static class ReShadeSetup
{
    private const int MaxPageBytes = 2_000_000;
    private const long MaxInstallerBytes = 100L * 1024 * 1024;
    private static readonly Uri Homepage = new("https://reshade.me/");
    private static readonly Regex InstallerName = new(@"^ReShade_Setup_[0-9]+(?:\.[0-9]+){1,3}\.exe$", RegexOptions.CultureInvariant);
    private static readonly Regex Links = new("<a\\b[^>]*?\\s+href\\s*=\\s*(?:\"(?<url>[^\"]*)\"|'(?<url>[^']*)'|(?<url>[^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static Uri FindOfficialDownload(string html)
    {
        if (html is null || html.Length > MaxPageBytes) throw new InvalidOperationException(L.T("ReShadeDownloadNotFound"));
        // Ignore commented and script/style content, which is not an actual download anchor.
        html = Regex.Replace(html, @"<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        foreach (Match link in Links.Matches(html))
        {
            var href = WebUtility.HtmlDecode(link.Groups["url"].Value);
            if (href.Contains('%') || href.Contains('\\')) continue;
            if (Uri.TryCreate(Homepage, href, out var uri) && IsOfficial(uri) && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && uri.AbsolutePath == "/downloads/" + Path.GetFileName(uri.AbsolutePath)
                && InstallerName.IsMatch(Path.GetFileName(uri.AbsolutePath))) return uri;
        }
        throw new InvalidOperationException(L.T("ReShadeDownloadNotFound"));
    }

    public static async Task<string> DownloadAsync(string cacheRoot, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AtlasCollectionManager/1.0");
        using var page = await GetOfficialAsync(client, Homepage, cancellationToken);
        await using var pageStream = await page.Content.ReadAsStreamAsync(cancellationToken);
        using var pageBytes = new MemoryStream();
        await CopyBoundedAsync(pageStream, pageBytes, MaxPageBytes, null, null, cancellationToken);
        var uri = FindOfficialDownload(Encoding.UTF8.GetString(pageBytes.ToArray()));
        var folder = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(uri.AbsolutePath));
        var temporary = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var response = await GetOfficialAsync(client, uri, cancellationToken);
            var length = response.Content.Headers.ContentLength;
            if (length is <= 0 or > MaxInstallerBytes) throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await CopyBoundedAsync(input, output, MaxInstallerBytes, length, progress, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            // Reject empty/truncated responses and non-Windows payloads before replacing a cache entry.
            using (var check = File.OpenRead(temporary))
                if (check.Length < 2 || check.ReadByte() != 'M' || check.ReadByte() != 'Z') throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
            progress?.Report(1);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ProcessStartInfo Prepare(GameProfile profile, string gameFolder, string installerPath)
    {
        var game = GameLauncher.Prepare(profile, gameFolder);
        installerPath = Path.GetFullPath(installerPath);
        if (!InstallerName.IsMatch(Path.GetFileName(installerPath)) || !File.Exists(installerPath))
            throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
        var setup = new ProcessStartInfo { FileName = installerPath, WorkingDirectory = Path.GetDirectoryName(installerPath)!, UseShellExecute = true };
        setup.ArgumentList.Add(game.FileName);
        return setup;
    }

    private static bool IsOfficial(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host.Equals("reshade.me", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.reshade.me", StringComparison.OrdinalIgnoreCase));

    private static async Task<HttpResponseMessage> GetOfficialAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsOfficial(uri)) throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) break;
                uri = new Uri(uri, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, long? expected, IProgress<double>? progress, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > maximum) throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            if (expected is > 0) progress?.Report(Math.Min(.99, (double)total / expected.Value));
        }
        if (expected.HasValue && total != expected.Value) throw new InvalidOperationException(L.T("ReShadeSetupFailed"));
    }
}

