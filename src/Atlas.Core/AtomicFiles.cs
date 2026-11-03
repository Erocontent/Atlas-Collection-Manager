using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Atlas.Core.Tests")]
namespace Atlas.Core;

internal static class AtomicFiles
{
    internal static void Write(string target, Action<Stream> write)
    {
        SafePaths.NoLinks(target);
        var temporary = target + ".atlas-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            SafePaths.NoLinks(target);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void Copy(string source, string target) => Write(target, output => { using var input = File.OpenRead(source); input.CopyTo(output); });
}
