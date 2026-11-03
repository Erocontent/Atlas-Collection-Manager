using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Atlas.App;

public interface INexusCredentials { string? Load(); void Save(string key); void Delete(); }
public sealed class NexusCredentialVault(string target = "AtlasCollectionManager/Nexus") : INexusCredentials
{
    public string? Load()
    {
        if (!CredRead(target, 1, 0, out var pointer)) { if (Marshal.GetLastWin32Error() == 1168) return null; throw new Win32Exception(); }
        try { var credential = Marshal.PtrToStructure<Credential>(pointer); var bytes = new byte[credential.BlobSize]; Marshal.Copy(credential.Blob, bytes, 0, bytes.Length); try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes); } }
        finally { CredFree(pointer); }
    }
    public void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) > 2560 || key.Any(char.IsControl)) throw new InvalidOperationException("Invalid Nexus key.");
        var bytes = Encoding.UTF8.GetBytes(key); var pointer = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); var credential = new Credential { Type = 1, TargetName = target, BlobSize = (uint)bytes.Length, Blob = pointer, Persist = 2, UserName = "Nexus API" }; if (!CredWrite(ref credential, 0)) throw new Win32Exception(); }
        finally { for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(pointer, i, 0); Marshal.FreeHGlobal(pointer); Array.Clear(bytes); }
    }
    public void Delete() { if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type; public string TargetName; public string? Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize; public nint Blob; public uint Persist, AttributeCount; public nint Attributes; public string? TargetAlias; public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out nint pointer);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint pointer);
}

public sealed class NxmProtocol(string data, string executable, RegistryKey? testClasses = null)
{
    private sealed record Value(string Name, RegistryValueKind Kind, string? Text, string[]? Texts, byte[]? Bytes, long Number);
    private sealed record Node(Value[] Values, Dictionary<string, Node> Children);
    private sealed record Backup(string Command, Node? Previous);
    private string BackupPath => Path.Combine(data, "nxm-handler-backup.json");
    private string Command => "\"" + Path.GetFullPath(executable) + "\" \"%1\"";
    private RegistryKey Classes() => testClasses == null ? Registry.CurrentUser.CreateSubKey("Software\\Classes") : testClasses.OpenSubKey("", true)!;
    public bool IsRegistered { get { using var classes = Classes(); using var key = classes.OpenSubKey("nxm\\shell\\open\\command"); return Equals(key?.GetValue(""), Command); } }
    public void Register()
    {
        using var classes = Classes();
        using (var current = classes.OpenSubKey("nxm"))
        {
            if (File.Exists(BackupPath))
            {
                var old = ReadBackup(); using var command = current?.OpenSubKey("shell\\open\\command");
                if (!Equals(command?.GetValue(""), old.Command)) throw new InvalidOperationException("The NXM handler changed. Restore your previous Atlas association before registering again.");
                WriteBackup(old with { Command = Command });
            }
            else WriteBackup(new(Command, current == null ? null : Capture(current, 0)));
        }
        // The full previous tree is already durable; remove alternate COM/DDE handlers before setting ours.
        classes.DeleteSubKeyTree("nxm", false);
        using var scheme = classes.CreateSubKey("nxm"); scheme.SetValue("", "URL:Atlas Collection Manager"); scheme.SetValue("URL Protocol", "");
        using var commandKey = scheme.CreateSubKey("shell\\open\\command"); commandKey.SetValue("", Command);
        if(testClasses==null)SHChangeNotify(0x08000000,0,0,0);
    }
    public void Restore()
    {
        if (!File.Exists(BackupPath)) throw new InvalidOperationException("No saved NXM association.");
        var backup = ReadBackup(); using var classes = Classes();
        using (var current = classes.OpenSubKey("nxm\\shell\\open\\command")) if (!Equals(current?.GetValue(""), backup.Command)) throw new InvalidOperationException("Another application changed the NXM association. Atlas will not replace that choice.");
        classes.DeleteSubKeyTree("nxm", false);
        if (backup.Previous != null) { using var previous = classes.CreateSubKey("nxm"); RestoreNode(previous, backup.Previous); }
        File.Delete(BackupPath);
        if(testClasses==null)SHChangeNotify(0x08000000,0,0,0);
    }
    private Backup ReadBackup()
    {
        if (new FileInfo(BackupPath).Length > 1024 * 1024) throw new InvalidOperationException("Invalid NXM backup.");
        return JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) ?? throw new InvalidOperationException("Invalid NXM backup.");
    }
    private void WriteBackup(Backup backup)
    {
        Directory.CreateDirectory(data); var temp = BackupPath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, backup); stream.Flush(true); }
        File.Move(temp, BackupPath, true);
    }
    private static Node Capture(RegistryKey key, int depth)
    {
        if (depth > 16 || key.SubKeyCount > 100 || key.ValueCount > 100) throw new InvalidOperationException("NXM association is too complex to preserve.");
        var values = key.GetValueNames().Select(name =>
        {
            var kind = key.GetValueKind(name); var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is string text && text.Length > 32768 || value is byte[] bytes && bytes.Length > 65536) throw new InvalidOperationException("NXM association value is too large.");
            return new Value(name, kind, value as string, value as string[], value as byte[], value is int i ? i : value is long l ? l : 0);
        }).ToArray();
        var children = new Dictionary<string, Node>(); foreach (var name in key.GetSubKeyNames()) { using var child = key.OpenSubKey(name)!; children.Add(name, Capture(child, depth + 1)); } return new(values, children);
    }
    private static void RestoreNode(RegistryKey key, Node node)
    {
        foreach (var v in node.Values) key.SetValue(v.Name, v.Kind switch { RegistryValueKind.String or RegistryValueKind.ExpandString => v.Text!, RegistryValueKind.MultiString => v.Texts!, RegistryValueKind.DWord => (object)(int)v.Number, RegistryValueKind.QWord => v.Number, _ => v.Bytes! }, v.Kind);
        foreach (var child in node.Children) { using var nested = key.CreateSubKey(child.Key); RestoreNode(nested, child.Value); }
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId,uint flags,nint item1,nint item2);
}
