// OriginalSaveSlots.cs — three native run slots plus the durable profile (SPEC §1, §3.12).
// ENTRY POINT for the Application layer: construct one per channel with the storage
// adapter (PlayerPrefs/file on Unity, OriginalMemorySaveStorage in tests), call
// MigrateLegacy() once at boot, then List/Save/Load/Delete/Copy slots and
// LoadProfile/SaveProfile/RecordResult for the profile and its 20-result archive.
// Each slot is its own OriginalSaveJournal (checksum, previous-good backup, verified
// write), keyed "<legacy run key>.slot<n>". The legacy single-run key is only read,
// never modified: migration copies its primary and backup into slot 0 byte-for-byte.
// The profile keeps its existing key, so current profiles carry over untouched.
// Records are deterministic: fixed property order, no whitespace, injected clock.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public enum OriginalSaveSlotState { Empty, Ready, RecoveredBackup, Corrupt }
    public enum OriginalLegacyMigration { AlreadyDone, NoLegacySave, Migrated, SlotOccupied, LegacyUnreadable, WriteFailed }
    public sealed class OriginalSaveSlotMeta
    {
        public string ClassId; public int Act; public int Floor; public string Seed; public long PlaytimeSeconds;
        public string LastSavedUtc; public string Version; public string RunId; public string Origin;
        public JObject ToJson() => new JObject { ["class"] = ClassId, ["act"] = Act, ["floor"] = Floor, ["seed"] = Seed, ["playtimeSeconds"] = PlaytimeSeconds, ["lastSavedUtc"] = LastSavedUtc, ["version"] = Version, ["runId"] = RunId, ["origin"] = Origin };
        public static OriginalSaveSlotMeta FromJson(JObject meta)
        {
            if (meta == null) throw new ArgumentException("Missing save slot metadata.");
            long Number(string key) { var token = meta[key]; if (token == null || token.Type != JTokenType.Integer || (long)token < 0) throw new ArgumentException("Save slot metadata " + key + " must be a nonnegative integer."); return (long)token; }
            string Text(string key) { var token = meta[key]; if (token != null && token.Type != JTokenType.String && token.Type != JTokenType.Null) throw new ArgumentException("Save slot metadata " + key + " must be text."); return (string)token; }
            return new OriginalSaveSlotMeta { ClassId = Text("class"), Act = checked((int)Number("act")), Floor = checked((int)Number("floor")), Seed = Text("seed"), PlaytimeSeconds = Number("playtimeSeconds"), LastSavedUtc = Text("lastSavedUtc"), Version = Text("version"), RunId = Text("runId"), Origin = Text("origin") };
        }
    }
    public sealed class OriginalSaveSlotInfo
    {
        public int Slot; public OriginalSaveSlotState State; public OriginalSaveSlotMeta Meta;
    }
    public sealed class OriginalSaveSlots
    {
        public const int SlotCount = 3;
        private const int RecordSchemaVersion = 1;
        private readonly IOriginalSaveStorage _storage;
        private readonly string _version;
        private readonly Func<DateTime> _utcNow;
        private readonly OriginalSaveJournal[] _slots = new OriginalSaveJournal[SlotCount];
        private readonly OriginalSaveJournal _legacy, _profile;
        public OriginalSaveSlots(IOriginalSaveStorage storage, string channel, string buildVersion, Func<DateTime> utcNow = null)
        {
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("A save channel is required.");
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _version = buildVersion ?? ""; _utcNow = utcNow ?? (() => DateTime.UtcNow);
            LegacyRunKey = "AshenSpire.Unity.Original.v1." + channel; ProfileKey = "AshenSpire.Unity.Profile.v1." + channel; IndexKey = LegacyRunKey + ".slots";
            _legacy = new OriginalSaveJournal(LegacyRunKey, storage); _profile = new OriginalSaveJournal(ProfileKey, storage);
            for (var i = 0; i < SlotCount; i++) _slots[i] = new OriginalSaveJournal(SlotKey(i), storage);
        }
        public string LegacyRunKey { get; }
        public string ProfileKey { get; }
        public string IndexKey { get; }
        public string SlotKey(int slot) => LegacyRunKey + ".slot" + Check(slot).ToString(CultureInfo.InvariantCulture);
        private static int Check(int slot) { if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot), "Save slots are numbered 0 to " + (SlotCount - 1) + "."); return slot; }

        // ---- migration ----------------------------------------------------------
        // Copies the one-run save into an empty slot 0: legacy backup first, then legacy
        // primary, so slot 0 ends with the same current and previous snapshots. The
        // legacy keys are left in place for older builds. Runs once per channel.
        public OriginalLegacyMigration MigrateLegacy()
        {
            if (!string.IsNullOrEmpty(_storage.Read(IndexKey))) return OriginalLegacyMigration.AlreadyDone;
            if (!_legacy.HasSave) return Mark(OriginalLegacyMigration.NoLegacySave);
            if (_slots[0].HasSave) return Mark(OriginalLegacyMigration.SlotOccupied);
            var backup = Usable(_legacy.Peek(true)); var primary = Usable(_legacy.Peek(false));
            if (backup == null && primary == null) return OriginalLegacyMigration.LegacyUnreadable;
            foreach (var snapshot in new[] { backup, primary })
                if (snapshot != null && !_slots[0].Save(Record(snapshot, 0, "legacy")))
                { _slots[0].Clear(); return OriginalLegacyMigration.WriteFailed; }
            return Mark(OriginalLegacyMigration.Migrated);
        }
        private static JObject Usable(JObject snapshot) => snapshot != null && snapshot.Type == JTokenType.Object ? snapshot : null;
        private OriginalLegacyMigration Mark(OriginalLegacyMigration outcome)
        {
            _storage.Write(IndexKey, new JObject { ["schemaVersion"] = RecordSchemaVersion, ["legacyMigration"] = outcome.ToString(), ["atUtc"] = Stamp() }.ToString(Formatting.None));
            _storage.Flush(); return outcome;
        }

        // ---- slots ----------------------------------------------------------------
        public IReadOnlyList<OriginalSaveSlotInfo> List()
        {
            var rows = new List<OriginalSaveSlotInfo>();
            for (var i = 0; i < SlotCount; i++)
            {
                var info = new OriginalSaveSlotInfo { Slot = i, State = OriginalSaveSlotState.Empty };
                if (_slots[i].HasSave)
                {
                    info.State = OriginalSaveSlotState.Corrupt;
                    foreach (var backup in new[] { false, true })
                    {
                        var meta = TryMeta(_slots[i].Peek(backup));
                        if (meta == null) continue;
                        info.Meta = meta; info.State = backup ? OriginalSaveSlotState.RecoveredBackup : OriginalSaveSlotState.Ready; break;
                    }
                }
                rows.Add(info);
            }
            return rows;
        }
        // Playtime is the run's running total in seconds, tracked by the caller.
        // Returns false when the write did not read back; the slot then keeps its previous record.
        public bool Save(int slot, JObject snapshot, long playtimeSeconds)
        {
            Check(slot); if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (playtimeSeconds < 0) throw new ArgumentException("Playtime cannot be negative.");
            return _slots[slot].Save(Record(snapshot, playtimeSeconds, "slot"));
        }
        // validate is the caller's restore path (e.g. OriginalGameSession.Restore); records it rejects fall back to the backup.
        public JObject Load(int slot, Action<JObject> validate, out OriginalSaveSlotMeta meta, out bool recovered)
        {
            Check(slot);
            var record = _slots[slot].Load(candidate => { ValidateRecord(candidate); validate?.Invoke((JObject)candidate["snapshot"].DeepClone()); }, out recovered);
            meta = OriginalSaveSlotMeta.FromJson((JObject)record["meta"]); return (JObject)record["snapshot"];
        }
        public void Delete(int slot) => _slots[Check(slot)].Clear();
        // Copies the loadable record (metadata unchanged) into another slot. An occupied
        // target needs overwrite; its previous good record becomes the target's backup.
        public bool Copy(int from, int to, bool overwrite = false)
        {
            Check(from); Check(to); if (from == to) throw new ArgumentException("Copy needs two different slots.");
            if (_slots[to].HasSave && !overwrite) throw new InvalidOperationException("Save slot " + to + " is occupied.");
            var record = _slots[from].Load(ValidateRecord, out _);
            return _slots[to].Save(record);
        }

        // ---- profile and result archive -------------------------------------------
        // A missing profile starts fresh; an unreadable one throws with its bytes preserved (never silently empty).
        public OriginalProfile LoadProfile(OriginalContentCatalog catalog, out bool recovered)
        {
            recovered = false; if (!_profile.HasSave) return new OriginalProfile(catalog);
            return OriginalProfile.Restore(catalog, _profile.Load(value => OriginalProfile.Restore(catalog, value), out recovered));
        }
        public bool SaveProfile(OriginalProfile profile) => _profile.Save((profile ?? throw new ArgumentNullException(nameof(profile))).Snapshot());
        // Finishes the run in the profile (FIFO archive of 20, keyed by run ID) and persists it.
        public JObject RecordResult(OriginalProfile profile, JObject run, bool victory)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var receipt = profile.Finish((string)run?["runId"], run, victory);
            receipt["saved"] = (bool)receipt["duplicate"] || SaveProfile(profile); return receipt;
        }

        // ---- record format ------------------------------------------------------
        private JObject Record(JObject snapshot, long playtimeSeconds, string origin)
        {
            var run = snapshot["run"] as JObject ?? snapshot;
            int Count(JToken token, int fallback) => token != null && token.Type == JTokenType.Integer && (long)token >= 0 && (long)token <= int.MaxValue ? (int)token : fallback;
            var seed = run["seedString"] ?? run["seed"];
            var meta = new OriginalSaveSlotMeta
            {
                ClassId = (string)(run["classId"] ?? run["class"]), Act = Count(run["actNumber"] ?? run["act"], 1), Floor = Count(run["floor"], 0),
                Seed = seed == null || seed.Type == JTokenType.Null ? null : seed.Type == JTokenType.String ? (string)seed : seed.ToString(Formatting.None),
                PlaytimeSeconds = playtimeSeconds, LastSavedUtc = Stamp(), Version = _version, RunId = run["runId"]?.Type == JTokenType.String ? (string)run["runId"] : null, Origin = origin
            };
            return new JObject { ["schemaVersion"] = RecordSchemaVersion, ["meta"] = meta.ToJson(), ["snapshot"] = snapshot.DeepClone() };
        }
        private static void ValidateRecord(JObject record)
        {
            if ((int?)record["schemaVersion"] != RecordSchemaVersion) throw new ArgumentException("Unsupported save slot record version.");
            if (!(record["snapshot"] is JObject)) throw new ArgumentException("Save slot record has no snapshot.");
            OriginalSaveSlotMeta.FromJson(record["meta"] as JObject);
        }
        private static OriginalSaveSlotMeta TryMeta(JObject record)
        {
            if (record == null) return null;
            try { ValidateRecord(record); return OriginalSaveSlotMeta.FromJson((JObject)record["meta"]); }
            catch (ArgumentException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (OverflowException) { return null; }
        }
        // An Unspecified clock value is taken as UTC so an injected test clock never depends on the host zone.
        private string Stamp() { var now = _utcNow(); return (now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture); }
    }
}
