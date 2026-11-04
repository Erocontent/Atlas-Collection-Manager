using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Atlas.Core;

public static class NexusGames
{
    private static readonly Dictionary<string,string> Domains=new(StringComparer.OrdinalIgnoreCase){["doa5"]="deadoralive5",["dmc5"]="devilmaycry5",["re4"]="residentevil42023",["re3"]="residentevil32020",["re2"]="residentevil22019",["village"]="residentevilvillage",["mhwilds"]="monsterhunterwilds"};
    public static string Domain(string gameId)=>Domains.TryGetValue(gameId,out var d)?d:throw new InvalidOperationException("Unsupported Nexus game.");
    public static string GameId(string domain)=>Domains.FirstOrDefault(x=>string.Equals(x.Value,domain,StringComparison.OrdinalIgnoreCase)).Key??throw new InvalidOperationException("Unsupported Nexus game.");
}
public record NexusLink(string GameDomain,long ModId,long FileId,string? Key=null,long? Expires=null,long? UserId=null)
{
    public static NexusLink Parse(string value,DateTimeOffset? now=null)
    {
        if(value.Any(char.IsControl)||value.Contains('\\')||value.Length>8192||!Uri.TryCreate(value,UriKind.Absolute,out var u)||u.Scheme!="nxm"||u.UserInfo!=""||!u.IsDefaultPort||u.Fragment!="")throw new InvalidOperationException("Invalid NXM link.");
        if(!Regex.IsMatch(value,@"^nxm://[a-zA-Z0-9]+/mods/[1-9][0-9]*/files/[1-9][0-9]*(\?.*)?$"))throw new InvalidOperationException("Invalid NXM link.");
        var m=Regex.Match(u.AbsolutePath,@"^/mods/([1-9][0-9]*)/files/([1-9][0-9]*)$");
        if(!m.Success||!long.TryParse(m.Groups[1].Value,out var mod)||!long.TryParse(m.Groups[2].Value,out var file))throw new InvalidOperationException("Invalid NXM identifiers.");
        var domain=NexusGames.Domain(NexusGames.GameId(u.Host));var q=new Dictionary<string,string>(StringComparer.Ordinal);
        if(u.Query.Length>1)foreach(var pair in u.Query[1..].Split('&')){var p=pair.Split('=',2);if(p.Length!=2||p[0] is not("key" or "expires" or "user_id" or "view")||!q.TryAdd(p[0],Uri.UnescapeDataString(p[1])))throw new InvalidOperationException("Invalid NXM parameters.");}
        if(q.Remove("view",out var view)&&view!="1")throw new InvalidOperationException("Invalid NXM view parameter.");
        if(q.Count==0)return new(domain,mod,file);
        if(q.Count!=3||!q.TryGetValue("key",out var key)||!Regex.IsMatch(key,@"^[A-Za-z0-9_-]{1,1024}$")||!long.TryParse(q["expires"],out var expires)||expires<=(now??DateTimeOffset.UtcNow).ToUnixTimeSeconds()||!long.TryParse(q["user_id"],out var user)||user<=0)throw new InvalidOperationException("Incomplete or expired NXM authorization.");
        return new(domain,mod,file,key,expires,user);
    }
    public override string ToString()=>$"nxm://{GameDomain}/mods/{ModId}/files/{FileId}";
    internal void Validate()=>Parse($"nxm://{GameDomain}/mods/{ModId}/files/{FileId}"+(Key!=null?$"?key={Uri.EscapeDataString(Key)}&expires={Expires}&user_id={UserId}":Expires!=null||UserId!=null?"?invalid=1":""));
}
public record NexusUser(long UserId,string Name,bool IsPremium);
public record NexusFile(long FileId,string Name,string FileName,string Version,long SizeBytes);
public record NexusProgress(long Bytes,long? TotalBytes);
public record NexusDownload(string Path,NexusFile File,string GameId);

public sealed partial class NexusApiClient(HttpClient api,HttpClient downloads,string apiKey):IDisposable
{
    const long Limit=4L*1024*1024*1024;bool owns;DateTimeOffset retryAfter;
    public static NexusApiClient Create(string apiKey){var c=new NexusApiClient(new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)},new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan},apiKey);c.owns=true;return c;}
    public void Dispose(){if(owns){api.Dispose();downloads.Dispose();}}
    public async Task<NexusUser> ValidateAsync(CancellationToken cancellationToken=default){using var j=await Get("users/validate.json",cancellationToken);var r=j.RootElement;var id=r.GetProperty("user_id").GetInt64();if(id<=0)throw new InvalidOperationException("Invalid Nexus account response.");return new(id,r.GetProperty("name").GetString()??"",r.GetProperty("is_premium").GetBoolean());}
    string Prefix(NexusLink l)=>$"games/{l.GameDomain}/mods/{l.ModId}/files/{l.FileId}";
    public async Task<NexusFile> GetFileAsync(NexusLink link,CancellationToken cancellationToken=default){link.Validate();using var j=await Get(Prefix(link)+".json",cancellationToken);var r=j.RootElement;var size=r.TryGetProperty("size_in_bytes",out var b)&&b.ValueKind!=JsonValueKind.Null?b.GetInt64():0;var f=new NexusFile(r.GetProperty("file_id").GetInt64(),r.GetProperty("name").GetString()??"",r.GetProperty("file_name").GetString()??"",r.GetProperty("version").GetString()??"",size);if(f.FileId!=link.FileId||size<0||size>Limit)throw new InvalidOperationException("Invalid Nexus file metadata.");return f;}
    async Task<JsonDocument> Get(string path,CancellationToken ct)
    {
        var wait=retryAfter-DateTimeOffset.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait,ct);
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.nexusmods.com/v1/"+path);request.Headers.Add("apikey",apiKey);request.Headers.Add("Application-Name","AtlasCollectionManager");request.Headers.Add("Application-Version","0.19.0");
        try{using var response=await api.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);if(response.StatusCode==HttpStatusCode.TooManyRequests){var delay=response.Headers.RetryAfter?.Delta??(response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow)??TimeSpan.FromMinutes(1);retryAfter=DateTimeOffset.UtcNow+(delay>TimeSpan.Zero?delay:TimeSpan.FromSeconds(1));throw new InvalidOperationException("Nexus rate limit reached. Retry later.");}if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Nexus request failed ({(int)response.StatusCode}).");if(response.Content.Headers.ContentLength>1024*1024)throw new InvalidOperationException("Nexus response is too large.");await using var s=await response.Content.ReadAsStreamAsync(ct);using var ms=new MemoryStream();var buffer=new byte[8192];int n;while((n=await s.ReadAsync(buffer,ct))!=0){if(ms.Length+n>1024*1024)throw new InvalidOperationException("Nexus response is too large.");ms.Write(buffer,0,n);}return JsonDocument.Parse(ms.ToArray());}catch(HttpRequestException){throw new InvalidOperationException("Nexus connection failed.");}catch(JsonException){throw new InvalidOperationException("Invalid Nexus response.");}
    }
    static bool Safe(Uri u)=>u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo==""&&u.Fragment==""&&(u.Host=="cf-files.nexusmods.com"||u.Host=="nexus-cdn.com"||u.Host.EndsWith(".nexus-cdn.com",StringComparison.OrdinalIgnoreCase));
    static async Task<int> ReadDownload(Stream stream,byte[] buffer,CancellationToken ct){using var idle=CancellationTokenSource.CreateLinkedTokenSource(ct);idle.CancelAfter(TimeSpan.FromSeconds(60));return await stream.ReadAsync(buffer,idle.Token);}
    async Task<HttpResponseMessage> DownloadResponse(Uri url,CancellationToken ct)
    {
        for(var hop=0;hop<=5;hop++)
        {
            if(!Safe(url))throw new InvalidOperationException("Untrusted Nexus download host.");
            using var request=new HttpRequestMessage(HttpMethod.Get,url);var response=await downloads.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is >=300 and <400){var location=response.Headers.Location;response.Dispose();if(location==null||hop==5)throw new InvalidOperationException("Invalid Nexus download redirect.");url=location.IsAbsoluteUri?location:new Uri(url,location);continue;}
            return response;
        }
        throw new InvalidOperationException("Invalid Nexus download redirect.");
    }
    public async Task<NexusDownload> DownloadAsync(NexusLink link,string directory,IProgress<NexusProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        link.Validate();var user=await ValidateAsync(cancellationToken);if(!user.IsPremium&&(link.Key==null||link.UserId!=user.UserId))throw new InvalidOperationException("Free accounts require a current NXM link for the connected account.");if(link.UserId!=null&&link.UserId!=user.UserId)throw new InvalidOperationException("NXM link belongs to another account.");var file=await GetFileAsync(link,cancellationToken);var ext=Path.GetExtension(file.FileName).ToLowerInvariant();if(ext is not(".zip" or ".rar" or ".7z"))throw new InvalidOperationException("Unsupported Nexus archive type.");
        using var links=await Get(Prefix(link)+"/download_link.json"+(link.Key!=null?$"?key={Uri.EscapeDataString(link.Key)}&expires={link.Expires}":""),cancellationToken);var url=links.RootElement.EnumerateArray().Select(e=>e.GetProperty("URI").GetString()).Select(s=>Uri.TryCreate(s,UriKind.Absolute,out var u)?u:null).FirstOrDefault(u=>u!=null&&Safe(u));if(url==null)throw new InvalidOperationException("Nexus returned no trusted download host.");
        Directory.CreateDirectory(directory);var final=Path.Combine(directory,Guid.NewGuid().ToString("N")+ext);var part=final+".part";
        try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromMinutes(30));cancellationToken=deadline.Token;using var response=await DownloadResponse(url,cancellationToken);if(!response.IsSuccessStatusCode||response.RequestMessage?.RequestUri is Uri actual&&!Safe(actual))throw new InvalidOperationException("Nexus download failed.");var length=response.Content.Headers.ContentLength;if(length>Limit||length<=0)throw new InvalidOperationException("Invalid download length.");await using(var input=await response.Content.ReadAsStreamAsync(cancellationToken))await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){var buffer=new byte[81920];long total=0;int count;while((count=await ReadDownload(input,buffer,cancellationToken))!=0){total+=count;if(total>Limit||length!=null&&total>length)throw new InvalidOperationException("Download exceeds size limit.");await output.WriteAsync(buffer.AsMemory(0,count),cancellationToken);progress?.Report(new(total,length??(file.SizeBytes>0?file.SizeBytes:null)));}if(total==0||length!=null&&total!=length||file.SizeBytes>0&&total!=file.SizeBytes)throw new InvalidOperationException("Incomplete Nexus download.");}File.Move(part,final);return new(final,file,NexusGames.GameId(link.GameDomain));}catch(HttpRequestException){throw new InvalidOperationException("Nexus download connection failed.");}finally{if(File.Exists(part))File.Delete(part);}
    }
}
