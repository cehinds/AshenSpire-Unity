// OriginalSaveJournal.cs — checksummed current/previous native-run records.
// The application supplies storage (PlayerPrefs on Unity) as delegates or as an
// IOriginalSaveStorage. No platform API or current content is used here. Frozen
// content/rules live inside the snapshot. Loading validates through the caller's
// restore path before accepting a record. Corrupt bytes remain untouched;
// recovery never replaces the surviving backup.
// VERIFIED WRITE: every Save reads the primary back and compares it byte-for-byte.
// On mismatch or storage exception restoration of the previous primary is tried,
// the backup is left alone and Save returns false. A corrupt primary about to be
// overwritten is first copied to "<key>.corrupt" so the evidence survives.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalSaveJournal
    {
        private readonly IOriginalSaveStorage _storage;
        private readonly string _key;
        private bool _recoveredBackup;
        public OriginalSaveJournal(string key, Func<string,string> read, Action<string,string> write, Action flush)
            : this(key, new OriginalDelegateSaveStorage(read, write, flush)) { }
        public OriginalSaveJournal(string key, IOriginalSaveStorage storage)
        { _key = key ?? throw new ArgumentNullException(nameof(key)); _storage = storage ?? throw new ArgumentNullException(nameof(storage)); }
        public string Key => _key;
        public string BackupKey => _key + ".backup";
        public string CorruptKey => _key + ".corrupt";
        public bool HasSave => !string.IsNullOrEmpty(_storage.Read(_key)) || !string.IsNullOrEmpty(_storage.Read(BackupKey));
        public bool Save(JObject snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var envelope = Envelope(snapshot);
            string previous = null; var readPrevious = false;
            try
            {
                previous = _storage.Read(_key); readPrevious = true;
                var previousValid = Read(previous) != null;
                if (!_recoveredBackup && previousValid) _storage.Write(BackupKey, previous);
                else if ((!previousValid || _recoveredBackup) && !string.IsNullOrEmpty(previous)) _storage.Write(CorruptKey, previous);
                _storage.Write(_key, envelope); _storage.Flush();
                if (_storage.Read(_key) != envelope) { RestorePrevious(previous); return false; }
                _recoveredBackup = false; return true;
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                // A full/unavailable store can throw on write, flush or read-back.
                // Keep the validated backup and attempt to restore the old primary.
                // The caller retains its in-memory snapshot and reports/retries failure.
                if (readPrevious) RestorePrevious(previous);
                return false;
            }
        }
        private void RestorePrevious(string previous)
        {
            try
            {
                if (string.IsNullOrEmpty(previous)) _storage.Delete(_key); else _storage.Write(_key, previous);
                _storage.Flush();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { /* Storage is still unavailable; the backup was not rolled back or deleted. */ }
        }
        public JObject Load(Action<JObject> validate, out bool recovered)
        {
            foreach (var backup in new[] { false, true })
            {
                var candidate = Peek(backup);
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
        // Checksum-verified record from the primary or backup key, or null. No validation callback.
        public JObject Peek(bool backup) => Read(_storage.Read(backup ? BackupKey : _key));
        // Removes primary and backup (a player-chosen delete). Quarantined corrupt bytes are kept.
        public void Clear() { _storage.Delete(_key); _storage.Delete(BackupKey); _storage.Flush(); _recoveredBackup = false; }
        // Deterministic envelope bytes for a snapshot: property order as given, no whitespace.
        public static string Envelope(JObject snapshot)
        {
            var json = snapshot.ToString(Formatting.None);
            return new JObject { ["schemaVersion"] = 1, ["data"] = json, ["sha256"] = Digest(json) }.ToString(Formatting.None);
        }
        private static JObject Read(string envelope)
        {
            if (string.IsNullOrEmpty(envelope)) return null;
            try
            {
                var record = ParseStrict(envelope);
                var data = (string)record["data"];
                if ((int?)record["schemaVersion"] != 1 || data == null || (string)record["sha256"] != Digest(data)) return null;
                return ParseStrict(data);
            }
            catch (JsonException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidCastException) { return null; }
        }
        // Date-like strings stay strings, so a record re-serializes to the bytes it was read from.
        private static JObject ParseStrict(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonReaderException("Unexpected content after the saved record.");
                return value;
            }
        }
        private static string Digest(string data)
        { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(data))); }
    }
}
