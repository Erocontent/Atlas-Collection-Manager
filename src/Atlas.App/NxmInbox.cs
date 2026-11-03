using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Atlas.App;

public sealed class NxmInbox : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private static string PipeName => "Atlas-NXM-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];
    private readonly string pipeName;
    public NxmInbox(Action<string> receive, string? testPipeName = null) { pipeName = testPipeName ?? PipeName; _ = Listen(receive); }
    private async Task Listen(Action<string> receive)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var bytes = new byte[8193]; var length = 0;
                while (length < bytes.Length) { var count = await pipe.ReadAsync(bytes.AsMemory(length), timeout.Token); if (count == 0) break; length += count; }
                if (length is > 0 and <= 8192) receive(Encoding.UTF8.GetString(bytes, 0, length));
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
    public static async Task SendAsync(string link, string? testPipeName = null)
    {
        if (Encoding.UTF8.GetByteCount(link) > 8192) throw new InvalidOperationException("NXM link is too long.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var pipe = new NamedPipeClientStream(".", testPipeName ?? PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token); await pipe.WriteAsync(Encoding.UTF8.GetBytes(link), timeout.Token); await pipe.FlushAsync(timeout.Token);
    }
    public void Dispose() { stop.Cancel(); }
}
