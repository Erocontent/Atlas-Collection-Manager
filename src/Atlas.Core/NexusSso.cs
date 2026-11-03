using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Atlas.Core;
public static class NexusSso
{
    public static async Task<string> ConnectAsync(string registeredSlug,Action<Uri> openBrowser,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(registeredSlug)||!Regex.IsMatch(registeredSlug,@"^[A-Za-z0-9_-]{1,100}$"))throw new InvalidOperationException("An officially registered Nexus application slug is required.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(2));var ct=timeout.Token;
        using var socket=new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(new Uri("wss://sso.nexusmods.com"),ct);var id=Guid.NewGuid().ToString();
            await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,token=(string?)null,protocol=2})),WebSocketMessageType.Text,true,ct);
            var opened=false;var buffer=new byte[8192];
            while(true)
            {
                using var body=new MemoryStream();WebSocketReceiveResult message;
                do{message=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct);if(message.MessageType!=WebSocketMessageType.Text||body.Length+message.Count>65536)throw new InvalidOperationException("Invalid Nexus sign-in response.");body.Write(buffer,0,message.Count);}while(!message.EndOfMessage);
                using var json=JsonDocument.Parse(body.ToArray());var r=json.RootElement;
                if(!r.TryGetProperty("success",out var success)||!success.GetBoolean()||!r.TryGetProperty("data",out var data))throw new InvalidOperationException("Nexus sign-in failed.");
                if(data.TryGetProperty("api_key",out var key)){var value=key.GetString();if(string.IsNullOrWhiteSpace(value)||value.Length>4096||value.Any(char.IsControl))throw new InvalidOperationException("Invalid Nexus sign-in response.");return value;}
                if(!opened&&data.TryGetProperty("connection_token",out _)){opened=true;openBrowser(new Uri($"https://www.nexusmods.com/sso?id={id}&application={Uri.EscapeDataString(registeredSlug)}"));}
            }
        }
        catch(WebSocketException){throw new InvalidOperationException("Nexus sign-in connection failed.");}
        catch(JsonException){throw new InvalidOperationException("Invalid Nexus sign-in response.");}
    }
}
