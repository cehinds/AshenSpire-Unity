// LanStateStore.cs — private checksummed host save with atomic primary/backup writes.
// Never place this file under the static Web root. Corruption is an explicit error;
// recovery preserves the damaged primary before restoring a verified backup.
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Transport;

public static class LanStateStore
{
    private const long MaximumFileBytes = 64L * 1024 * 1024;
    public static JObject? Load(string? path)
    {
        if (path == null) return null;
        if (!File.Exists(path)) { if (File.Exists(path+".backup")) throw new InvalidDataException("host_state_missing_primary: backup exists; use --recover-backup true instead of resetting."); return null; }
        try
        {
            if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException("Save exceeds its maximum size.");
            using var reader = new JsonTextReader(File.OpenText(path)) { MaxDepth = 128, DateParseHandling = DateParseHandling.None };
            var envelope = JObject.Load(reader,new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || (string?)envelope["format"] != "ashenspire.native.lan" || (int?)envelope["version"] != 1 || envelope["payload"] is not JObject payload || !string.Equals((string?)envelope["sha256"],Digest(payload),StringComparison.Ordinal)) throw new InvalidDataException("Checksum or format mismatch.");
            return payload;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or OverflowException)
        { throw new InvalidDataException("host_state_corrupt: " + path + ". Existing files were preserved. Restore a verified backup explicitly with --recover-backup true.",error); }
    }
    private static string Digest(JObject payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)))).ToLowerInvariant();
    public static void Save(string? path,JObject payload)
    {
        if (path == null) return;
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var envelope = new JObject { ["format"] = "ashenspire.native.lan", ["version"] = 1, ["sha256"] = Digest(payload), ["payload"] = payload };
        var bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None)); if (bytes.Length > MaximumFileBytes) throw new IOException("host_state_too_large");
        var temporary = path + ".pending." + Guid.NewGuid().ToString("N");
        using (var file = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary,path,path+".backup",true); else File.Move(temporary,path);
    }
    public static string RecoverBackup(string path)
    {
        path = Path.GetFullPath(path);
        using var ownership = Acquire(path);
        _ = Load(path+".backup") ?? throw new InvalidDataException("host_backup_missing: " + path + ".backup");
        var evidence = path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N");
        if (File.Exists(path)) File.Copy(path,evidence,false);
        var temporary = path+".recover."+Guid.NewGuid().ToString("N");File.Copy(path+".backup",temporary,false);
        File.Move(temporary,path,true); return evidence;
    }
    internal static FileStream? Acquire(string? path)
    {
        if (path == null) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try { return new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch (IOException error) { throw new IOException("host_state_in_use: another companion owns this state file.",error); }
    }
}
