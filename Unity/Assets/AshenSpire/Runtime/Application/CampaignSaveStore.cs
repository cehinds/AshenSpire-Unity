// CampaignSaveStore.cs — PlayerPrefs boundary with checksum and previous valid record.
// RunController supplies a channel/schema-specific key. Browser site-data removal
// removes saves. Checksums detect corruption, not cheating; invalid records stay intact.
using System;
using System.Security.Cryptography;
using System.Text;
using AshenSpire.Domain;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed class CampaignSaveStore
    {
        [Serializable]
        private sealed class Envelope
        {
            public string Data; public string Hash;
        }
        private readonly string _key;
        public CampaignSaveStore(string key) => _key = key;
        public bool HasSave => PlayerPrefs.HasKey(_key) || PlayerPrefs.HasKey(_key + ".backup");
        public void Save(CampaignState state)
        {
            var data = JsonUtility.ToJson(state);
            var existing = PlayerPrefs.GetString(_key, "");
            if (TryRead(existing, out _))
                PlayerPrefs.SetString(_key + ".backup", existing);
            PlayerPrefs.SetString(_key, JsonUtility.ToJson(new Envelope { Data = data, Hash = Digest(data) }));
            PlayerPrefs.Save();
        }
        public CampaignState Load()
        {
            if (TryRead(PlayerPrefs.GetString(_key, ""), out var state))
                return state;
            if (TryRead(PlayerPrefs.GetString(_key + ".backup", ""), out state))
            {
                Debug.LogWarning("Recovered the previous campaign checkpoint.");
                return state;
            }
            throw new ArgumentException("Campaign checksum or saved JSON is invalid.");
        }
        private static bool TryRead(string json, out CampaignState state)
        {
            state = null;
            try
            {
                if (string.IsNullOrEmpty(json))
                    return false;
                var envelope = JsonUtility.FromJson<Envelope>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.Data) || Digest(envelope.Data) != envelope.Hash)
                    return false;
                state = JsonUtility.FromJson<CampaignState>(envelope.Data);
                return state != null;
            }
            catch { return false; }
        }
        private static string Digest(string value)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}
