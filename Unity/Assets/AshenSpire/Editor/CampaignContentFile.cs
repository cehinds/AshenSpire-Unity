// CampaignContentFile.cs — editor/CLI file transactions, independent from Unity APIs.
// Validate before writing; compare the loaded hash again immediately before replacement.
// Exact prior bytes remain in a unique backup. Import errors do not mean source save failed.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AshenSpire.Editor
{
    [Serializable]
    public sealed class CampaignDraftCheckpoint
    {
        public string DraftJson, SavedJson, SourceHash;
    }
    public sealed class CampaignSaveResult
    {
        public string SourceHash, BackupPath, ImportError;
        public bool Imported => ImportError == null;
    }
    public static class CampaignContentFile
    {
        public static string Hash(string path)
        {
            return Fingerprint(File.ReadAllBytes(path));
        }
        public static string Fingerprint(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static CampaignSaveResult Save(string source, string expectedHash, string candidate, string backupRoot, Action<string> validate, Action import)
        {
            validate(candidate);
            if (Hash(source) != expectedHash)
                throw new IOException("The source changed on disk. Your draft is retained. Reload or merge those changes before saving.");
            var backup = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            var previous = Path.Combine(backup, Path.GetFileName(source));
            Replace(source, candidate, previous, () =>
            {
                if (Hash(source) != expectedHash)
                    throw new IOException("The source changed while preparing the save. Draft retained.");
            });
            var result = new CampaignSaveResult { SourceHash = Hash(source), BackupPath = previous };
            try { import(); }
            catch (Exception error) { result.ImportError = error.Message; }
            return result;
        }
        public static void Checkpoint(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            var backup = path + ".previous-" + Guid.NewGuid().ToString("N") + ".json";
            Replace(path, json, backup, () => { });
        }
        private static void Replace(string path, string json, string backup, Action beforeReplace)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                beforeReplace();
                if (File.Exists(path)) File.Replace(temporary, path, backup);
                else File.Move(temporary, path);
            }
            finally
            {
                // This call owns this exact newly-created temporary path only.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
