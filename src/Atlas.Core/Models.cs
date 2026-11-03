namespace Atlas.Core;

public sealed record ModPackage(string Id, string Name, string Category, string Destination, string[] Files, int FormatVersion = 1, string Kind = "files", DateTimeOffset? ImportedAt = null,string[]? Characters=null);
public sealed record ManagedFile(string Path, string Hash, bool HadOriginal);
public sealed record ActiveMod(string Id, string Game, ManagedFile[] Files, string[]? CreatedDirectories = null);
public sealed record Journal(string Game, string Operation, ActiveMod Mod);
