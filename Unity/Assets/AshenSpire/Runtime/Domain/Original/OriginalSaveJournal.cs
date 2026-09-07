// OriginalSaveJournal.cs — checksummed current/previous native-run records.
// The application supplies storage delegates (PlayerPrefs on Unity). No platform
// API or current content is used here. Frozen content/rules live inside the snapshot.
// Loading validates through the caller's restore path before accepting a record.
// Corrupt bytes remain untouched; recovery never replaces the surviving backup.
using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalSaveJournal
    {
        private readonly Func<string, string> _read;
        private readonly Action<string, string> _write;
        private readonly Action _flush;
        private readonly string _key;
        private bool _recoveredBackup;
        public OriginalSaveJournal(string key, Func<string,string> read, Action<string,string> write, Action flush)
        { _key = key; _read = read; _write = write; _flush = flush; }
        public bool HasSave => !string.IsNullOrEmpty(_read(_key)) || !string.IsNullOrEmpty(_read(_key + ".backup"));
        public void Save(JObject snapshot)
        {
            var json = snapshot.ToString(Formatting.None);
            var envelope = new JObject { ["schemaVersion"] = 1, ["data"] = json, ["sha256"] = Digest(json) }.ToString(Formatting.None);
            var previous = _read(_key);
            if (!_recoveredBackup && Read(previous) != null) _write(_key + ".backup", previous);
            _write(_key, envelope); _flush(); _recoveredBackup = false;
        }
        public JObject Load(Action<JObject> validate, out bool recovered)
        {
            foreach (var backup in new[] { false, true })
            {
                var candidate = Read(_read(_key + (backup ? ".backup" : "")));
                if (candidate == null) continue;
                try { validate((JObject)candidate.DeepClone()); }
                catch (ArgumentException) { continue; }
                catch (InvalidOperationException) { continue; }
                catch (FormatException) { continue; }
                catch (InvalidCastException) { continue; }
                catch (OverflowException) { continue; }
                recovered = backup; _recoveredBackup = backup; return candidate;
            }
            recovered = false; throw new ArgumentException("No valid native run save could be restored. Existing records were preserved.");
        }
        private static JObject Read(string envelope)
        {
            if (string.IsNullOrEmpty(envelope)) return null;
            try
            {
                var record = JObject.Parse(envelope);
                var data = (string)record["data"];
                if ((int?)record["schemaVersion"] != 1 || data == null || (string)record["sha256"] != Digest(data)) return null;
                return JObject.Parse(data, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException) { return null; }
            catch (ArgumentException) { return null; }
        }
        private static string Digest(string data)
        { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(data))); }
    }
}
