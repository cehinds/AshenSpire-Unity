// Core2SaveSlots.cs — where a Core 2 run and the Core 2 profile are kept: three run slots and one profile record under
// their own keys, apart from every 0.5.5 key (those saves are dropped, never read: owner decision). Engine-free; the
// host hands it a key/value store (PlayerPrefs in the player, a dictionary in UnityTests/Core2RunView).
//
// KEYS (per channel: desktop, web, dev, test…):
//   AshenSpire.Core2.Run.v1.<channel>.slot<N>        the run save text (Core2RunSave.Serialize, format ashen.core2.run)
//   AshenSpire.Core2.Run.v1.<channel>.slot<N>.meta   { savedAt, classId, act, floor } for the slot list and Continue
//   AshenSpire.Core2.Profile.v1.<channel>            the profile save (Core2ProfileSave: the class unlock ledger)
// A save that cannot be loaded (another format, a newer build, other rules) is refused by name and its text is left
// exactly as it is (JS resumeRun: "The save is never touched").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>A string key/value store (PlayerPrefs, or a dictionary in tests).</summary>
    public interface ICore2Storage
    {
        string Get(string key);
        void Set(string key, string value);
        void Delete(string key);
        void Flush();
    }

    public sealed class Core2MemoryStorage : ICore2Storage
    {
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
        public void Flush() { }
    }

    public enum Core2SlotState { Empty, Ready, Refused }

    public sealed class Core2SlotInfo
    {
        public int Slot { get; set; }
        public Core2SlotState State { get; set; }
        public string ClassId { get; set; }
        public int Act { get; set; }
        public int Floor { get; set; }
        public DateTime SavedAt { get; set; }
        /// <summary>Why a refused slot cannot load (Core2SaveRefused message).</summary>
        public string Refusal { get; set; }
    }

    public sealed class Core2SaveSlots
    {
        public const int SlotCount = 3;
        private readonly ICore2Storage _storage;
        private readonly Core2Content _content;
        public string Channel { get; }

        public Core2SaveSlots(ICore2Storage storage, Core2Content content, string channel)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Channel = string.IsNullOrEmpty(channel) ? "desktop" : channel;
        }

        public string RunKey(int slot) => "AshenSpire.Core2.Run.v1." + Channel + ".slot" + (slot + 1).ToString(CultureInfo.InvariantCulture);
        public string MetaKey(int slot) => RunKey(slot) + ".meta";
        public string ProfileKey => "AshenSpire.Core2.Profile.v1." + Channel;

        public List<Core2SlotInfo> List()
        {
            var list = new List<Core2SlotInfo>();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var text = _storage.Get(RunKey(slot));
                var info = new Core2SlotInfo { Slot = slot, State = string.IsNullOrEmpty(text) ? Core2SlotState.Empty : Core2SlotState.Ready };
                if (info.State == Core2SlotState.Ready)
                {
                    try
                    {
                        var run = Core2RunSave.Deserialize(_content, text);
                        info.ClassId = run.Class; info.Act = run.ActNumber; info.Floor = run.Floor;
                    }
                    catch (Core2SaveRefused refused) { info.State = Core2SlotState.Refused; info.Refusal = refused.Message; }
                    try
                    {
                        var meta = JObject.Parse(_storage.Get(MetaKey(slot)) ?? "{}");
                        if (meta["savedAt"] != null) info.SavedAt = new DateTime((long)meta["savedAt"], DateTimeKind.Utc);
                    }
                    catch (JsonException) { }
                }
                list.Add(info);
            }
            return list;
        }

        /// <summary>The slot Continue resumes: the newest loadable save, the lowest slot on a tie; -1 when none.</summary>
        public int Latest() => List().Where(s => s.State == Core2SlotState.Ready).OrderByDescending(s => s.SavedAt).ThenBy(s => s.Slot).Select(s => s.Slot).DefaultIfEmpty(-1).First();
        /// <summary>The first slot holding nothing; -1 when all three hold a climb.</summary>
        public int FirstEmpty() => List().Where(s => s.State == Core2SlotState.Empty).Select(s => s.Slot).DefaultIfEmpty(-1).First();
        public bool HasContinue => Latest() >= 0;

        public void Save(int slot, Core2Run run, DateTime savedAtUtc)
        {
            _storage.Set(RunKey(slot), Core2RunSave.Serialize(_content, run));
            _storage.Set(MetaKey(slot), new JObject { ["savedAt"] = savedAtUtc.Ticks, ["classId"] = run.Class, ["act"] = run.ActNumber, ["floor"] = run.Floor }.ToString(Formatting.None));
            _storage.Flush();
        }

        /// <summary>Load a slot, or throw Core2SaveRefused naming why (the stored text is not touched).</summary>
        public Core2Run Load(int slot)
        {
            var text = _storage.Get(RunKey(slot));
            if (string.IsNullOrEmpty(text)) throw new Core2SaveRefused("empty", "Slot " + (slot + 1) + " holds no climb.");
            return Core2RunSave.Deserialize(_content, text);
        }

        public void Delete(int slot)
        {
            _storage.Delete(RunKey(slot));
            _storage.Delete(MetaKey(slot));
            _storage.Flush();
        }

        /// <summary>The profile (class unlock ledger). A refused record reads as an empty profile and Refusal says why;
        /// it is then never overwritten (ProfileWritable is false).</summary>
        public Core2Profile LoadProfile(out string refusal)
        {
            refusal = null;
            var text = _storage.Get(ProfileKey);
            if (string.IsNullOrEmpty(text)) return new Core2Profile();
            try { return Core2ProfileSave.Deserialize(text); }
            catch (Core2SaveRefused refused) { refusal = refused.Message; return new Core2Profile(); }
        }

        public void SaveProfile(Core2Profile profile)
        {
            _storage.Set(ProfileKey, Core2ProfileSave.Serialize(profile));
            _storage.Flush();
        }
    }
}
