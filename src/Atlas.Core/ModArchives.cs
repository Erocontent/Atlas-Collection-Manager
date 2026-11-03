using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Rar;
using SharpCompress.Common.SevenZip;

namespace Atlas.Core;

/// <summary>Managed, validated ZIP/RAR/7z access shared by import and inspection.</summary>
public static class ModArchives
{
    internal const long MaximumBytes = 4L * 1024 * 1024 * 1024;
    public static bool IsSupported(string path) => new[] { ".zip", ".rar", ".7z" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string[] PreviewFiles(string path, int maximum = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        using var archive = Open(path);
        return archive.Files.Take(maximum).Select(e => e.FullName).ToArray();
    }
    internal static Archive Open(string path) => new(path);

    internal sealed class Entry(IArchiveEntry entry, string name)
    {
        public string FullName { get; } = name;
        public long Length => entry.Size;
        public Stream Open() => entry.OpenEntryStream();
        public void CopyTo(Stream output, ref long total, long maximum = MaximumBytes)
        {
            using var input = Open();
            var buffer = new byte[81920]; long count = 0;
            int read;
            while ((read = input.Read(buffer)) != 0)
            {
                count = checked(count + read); total = checked(total + read);
                if (count > maximum || total > MaximumBytes) throw new InvalidOperationException(L.T("ZipTooLarge"));
                output.Write(buffer, 0, read);
            }
            if (count != Length) throw new InvalidOperationException("Archive entry size does not match its metadata.");
        }
    }
    internal sealed class Archive : IDisposable
    {
        private readonly Stream source;
        private IArchive? archive;
        public List<Entry> Files { get; } = [];
        public Archive(string path)
        {
            SafePaths.NoLinks(path);
            if (!IsSupported(path)) throw new InvalidOperationException("Choose a ZIP, RAR or 7z archive.");
            source = File.OpenRead(path);
            try
            {
                // Stream access deliberately avoids automatic discovery of adjacent volumes.
                archive = ArchiveFactory.OpenArchive(source);
                if (archive.Type is not (ArchiveType.Zip or ArchiveType.Rar or ArchiveType.SevenZip)) throw new InvalidOperationException("Unsupported archive format.");
                if (archive.IsEncrypted) throw new InvalidOperationException("Encrypted archives are not supported.");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0; int count = 0;
                foreach (var entry in archive.Entries)
                {
                    if (++count > 20000) throw new InvalidOperationException(L.T("TooManyEntries"));
                    if (entry.IsEncrypted) throw new InvalidOperationException("Encrypted archives are not supported.");
                    if (!entry.IsComplete || entry.IsSplitAfter || (archive.Type != ArchiveType.SevenZip && (entry.VolumeIndexFirst != 0 || entry.VolumeIndexLast != 0))) throw new InvalidOperationException("Split-volume archives are not supported.");
                    var attributes = entry.Attrib ?? 0;
                    var extended = (entry as SevenZipEntry)?.ExtendedAttrib ?? 0;
                    if (entry.LinkTarget != null || entry is RarEntry { IsRedir: true } ||
                        (attributes & 0x400) != 0 || ((attributes >> 16) & 0xF000) == 0xA000 ||
                        (attributes & 0xF000) == 0xA000 || (extended & 0xF000) == 0xA000)
                        throw new InvalidOperationException(L.T("ZipLink"));
                    var name = SafePaths.Relative((entry.Key ?? "").Replace('\\', '/').TrimEnd('/'));
                    if (!paths.Add(name)) throw new InvalidOperationException(L.T("ZipDuplicate"));
                    if (entry.Size < 0 || entry.Size > MaximumBytes - total) throw new InvalidOperationException(L.T("ZipTooLarge"));
                    total += entry.Size;
                    if (!entry.IsDirectory) Files.Add(new Entry(entry, name));
                }
                if (!archive.IsComplete) throw new InvalidOperationException("Split-volume or incomplete archives are not supported.");
                if (archive.Volumes.OfType<RarVolume>().Any(volume => volume.IsMultiVolume))
                    throw new InvalidOperationException("Split-volume archives are not supported.");
                // A file must never also be the parent directory of another file.
                var files = Files.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                    for (var slash = file.LastIndexOf('/'); slash >= 0; slash = file.LastIndexOf('/', slash - 1))
                        if (files.Contains(file[..slash])) throw new InvalidOperationException(L.T("ZipDuplicate"));
            }
            catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or SharpCompress.Common.CryptographicException)
            { Dispose(); throw new InvalidOperationException("Encrypted archives are not supported.", error); }
            catch { Dispose(); throw; }
        }
        public void Dispose() { archive?.Dispose(); source.Dispose(); }
    }
}
