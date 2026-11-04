using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Atlas.Core;

public static class CollectionDirectDownloads
{
    private const long MaxBytes = 8L * 1024 * 1024 * 1024;
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectPublicAsync
    });

    private static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] is > 0 and < 224 and not (10 or 127) &&
            !(b[0] == 100 && b[1] is >= 64 and < 128) &&
            !(b[0] == 169 && b[1] == 254) &&
            !(b[0] == 172 && b[1] is >= 16 and < 32) &&
            !(b[0] == 192 && b[1] is 0 or 168) &&
            !(b[0] == 198 && b[1] is 18 or 19);
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var endpoint = context.DnsEndPoint;
        foreach (var address in await Dns.GetHostAddressesAsync(endpoint.Host, token).ConfigureAwait(false))
        {
            if (!PublicAddress(address)) continue;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, endpoint.Port, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("The download host has no reachable public address.");
    }

    public static async Task<string> DownloadAsync(CollectionMod mod, string folder, IProgress<double>? progress, CancellationToken token)
    {
        if (!mod.DirectDownload || mod.Nexus != null || mod.SourceUrl == null || mod.Sha256 == null)
            throw new InvalidOperationException("The collection does not specify a verified direct download.");
        var uri = new Uri(mod.SourceUrl);
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".zip" or ".rar" or ".7z")) throw new InvalidOperationException("Unsupported archive extension.");
        Directory.CreateDirectory(folder);
        var path = SafePaths.Resolve(folder, Guid.NewGuid().ToString("N") + extension);
        var partial = path + ".part";
        try
        {
            for (var redirects = 0; redirects <= 5; redirects++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
                    throw new HttpRequestException("Direct downloads require public HTTPS on the default port.");
                using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is {} next)
                {
                    uri = new Uri(uri, next);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                if (length > MaxBytes) throw new IOException("The archive exceeds the direct download limit.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    var buffer = new byte[65536];
                    long received = 0;
                    int last = -1;
                    int count;
                    while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        received += count;
                        if (received > MaxBytes) throw new IOException("The archive exceeds the direct download limit.");
                        hash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        if (length > 0 && (int)(100d * received / length.Value) is var percent && percent != last)
                        {
                            last = percent;
                            progress?.Report(percent);
                        }
                    }
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Downloaded archive SHA-256 does not match the collection.");
                token.ThrowIfCancellationRequested();
                File.Move(partial, path);
                return path;
            }
            throw new HttpRequestException("Too many redirects for the direct download.");
        }
        finally { File.Delete(partial); }
    }
}
