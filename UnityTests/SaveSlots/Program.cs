// Save slot and profile archive checks for OriginalSaveSlots (F10, SPEC §3.12).
// Pure domain: in-memory and fault-injecting storage, fixed clock. Run from the repository root
// or pass the root as the first argument.
using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

try
{
    var root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
    var classId = (string)catalog.Table("classes").First()["id"]!;
    var passed = 0;
    void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
    void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL: expected " + typeof(T).Name + ": " + name); }
    var clock = new DateTime(2026, 9, 24, 12, 30, 45, DateTimeKind.Utc);
    DateTime Now() => clock;
    OriginalSaveSlots Slots(IOriginalSaveStorage storage) => new OriginalSaveSlots(storage, "web", "0.0.14.0", Now);
    JObject Snapshot(int floor, string runId = "run-a") => new JObject
    {
        ["schemaVersion"] = 1, ["content"] = new JObject { ["id"] = "frozen" }, ["supplement"] = new JObject(),
        ["run"] = new JObject { ["classId"] = classId, ["class"] = classId, ["actNumber"] = 2, ["floor"] = floor, ["seed"] = 4242u, ["seedString"] = "ASH-4242", ["runId"] = runId,
            ["note"] = "2026-01-02T03:04:05.123+02:00", ["ratio"] = 0.1, ["stats"] = new JObject { ["fightsWon"] = floor, ["damageDealt"] = 10, ["damageTaken"] = 3 } }
    };
    string Data(string envelope) => (string)JObject.Parse(envelope)["data"]!;
    string Bytes(JToken token) => token.ToString(Formatting.None);
    JObject Parse(string json) => JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;

    // ---- migration of the single-run save into slot 0 ----------------------------
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        var legacy = new OriginalSaveJournal(slots.LegacyRunKey, key => storage.Read(key) ?? "", (key, value) => storage.Write(key, value), storage.Flush);
        legacy.Save(Snapshot(3)); legacy.Save(Snapshot(4));
        var legacyPrimary = storage.Read(slots.LegacyRunKey)!; var legacyBackup = storage.Read(slots.LegacyRunKey + ".backup")!;
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.Migrated, "legacy single-run save migrates into slot 0");
        var loaded = slots.Load(0, null, out var meta, out var recovered);
        Check(Bytes(loaded) == Data(legacyPrimary) && !recovered, "migrated slot 0 snapshot is byte-identical to the legacy primary (date-like strings and floats kept)");
        var slotBackup = Parse(Data(storage.Read(slots.SlotKey(0) + ".backup")!));
        Check(Bytes(slotBackup["snapshot"]!) == Data(legacyBackup), "legacy backup generation becomes slot 0 backup");
        Check(storage.Read(slots.LegacyRunKey) == legacyPrimary && storage.Read(slots.LegacyRunKey + ".backup") == legacyBackup, "legacy keys are left untouched for older builds");
        Check(meta.ClassId == classId && meta.Act == 2 && meta.Floor == 4 && meta.Seed == "ASH-4242" && meta.RunId == "run-a" && meta.Origin == "legacy" && meta.PlaytimeSeconds == 0, "migrated metadata is derived from the run");
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.AlreadyDone, "migration runs once per channel");
        slots.Delete(0);
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.AlreadyDone && slots.List()[0].State == OriginalSaveSlotState.Empty, "a deleted slot 0 is not resurrected from the legacy save");
        var list = slots.List();
        Check(list.Count == OriginalSaveSlots.SlotCount && list[1].State == OriginalSaveSlotState.Empty && list[2].State == OriginalSaveSlotState.Empty, "other slots stay empty after migration");
    }
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.NoLegacySave && storage.Read(slots.IndexKey) != null, "a fresh install records that there was nothing to migrate");
    }
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        new OriginalSaveJournal(slots.LegacyRunKey, storage).Save(Snapshot(5));
        slots.Save(0, Snapshot(9, "run-new"), 60); var before = storage.Read(slots.SlotKey(0));
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.SlotOccupied && storage.Read(slots.SlotKey(0)) == before, "migration never overwrites an occupied slot 0");
    }
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        storage.Write(slots.LegacyRunKey, "{\"schemaVersion\":1,\"data\":\"{}\",\"sha256\":\"bad\"}"); storage.Write(slots.LegacyRunKey + ".backup", "garbage");
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.LegacyUnreadable && storage.Read(slots.LegacyRunKey + ".backup") == "garbage" && storage.Read(slots.IndexKey) == null && !storage.Values.ContainsKey(slots.SlotKey(0)), "unreadable legacy save is preserved, not migrated, and retried later");
    }
    {
        var inner = new OriginalMemorySaveStorage(); var faulty = new FaultyStorage(inner); var slots = Slots(faulty);
        var legacy = new OriginalSaveJournal(slots.LegacyRunKey, inner); legacy.Save(Snapshot(6)); var legacyBytes = inner.Read(slots.LegacyRunKey);
        faulty.BreakKey = slots.SlotKey(0);
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.WriteFailed && !slots.List()[0].State.Equals(OriginalSaveSlotState.Ready) && inner.Read(slots.IndexKey) == null && inner.Read(slots.LegacyRunKey) == legacyBytes, "a failed migration write leaves the legacy save intact and unmarked");
        faulty.BreakKey = null;
        Check(slots.MigrateLegacy() == OriginalLegacyMigration.Migrated && Bytes(slots.Load(0, null, out _, out _)) == Data(legacyBytes!), "migration succeeds on retry");
    }

    // ---- save/load roundtrip, metadata, deterministic bytes ------------------------
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage); var snapshot = Snapshot(7, "run-b");
        Check(slots.Save(1, snapshot, 3725), "save to slot 1 verifies");
        var loaded = slots.Load(1, value => { if (!(value["run"] is JObject)) throw new ArgumentException("no run"); }, out var meta, out var recovered);
        Check(JToken.DeepEquals(loaded, snapshot) && Bytes(loaded) == Bytes(snapshot) && !recovered, "slot roundtrip restores the exact snapshot");
        Check(meta.ClassId == classId && meta.Act == 2 && meta.Floor == 7 && meta.Seed == "ASH-4242" && meta.PlaytimeSeconds == 3725 && meta.LastSavedUtc == "2026-09-24T12:30:45Z" && meta.Version == "0.0.14.0" && meta.RunId == "run-b" && meta.Origin == "slot", "slot metadata records class, act/floor, seed, playtime, UTC time and version");
        var info = slots.List();
        Check(info[0].State == OriginalSaveSlotState.Empty && info[1].State == OriginalSaveSlotState.Ready && info[1].Meta!.Floor == 7 && info[2].State == OriginalSaveSlotState.Empty, "list reports per-slot state and metadata");
        var other = new OriginalMemorySaveStorage(); Slots(other).Save(1, Snapshot(7, "run-b"), 3725);
        Check(storage.Values.SequenceEqual(other.Values), "same inputs serialize to identical bytes");
        var seedOnly = Snapshot(1); ((JObject)seedOnly["run"]!).Remove("seedString"); slots.Save(2, seedOnly, 0);
        slots.Load(2, null, out var seedMeta, out _);
        Check(seedMeta.Seed == "4242", "numeric seed is recorded as invariant text");
        Throws<ArgumentOutOfRangeException>(() => slots.Save(3, snapshot, 0), "slot index above 2 is refused");
        Throws<ArgumentOutOfRangeException>(() => slots.Load(-1, null, out _, out _), "negative slot index is refused");
        Throws<ArgumentException>(() => slots.Save(0, snapshot, -1), "negative playtime is refused");
        var storageFlushes = storage.Flushes; slots.Save(1, Snapshot(8, "run-b"), 4000);
        Check(storage.Flushes == storageFlushes + 1, "a verified save flushes storage once");
        slots.Load(1, null, out var newer, out _);
        var backupRecord = Parse(Data(storage.Read(slots.SlotKey(1) + ".backup")!));
        Check(newer.Floor == 8 && newer.PlaytimeSeconds == 4000 && (int)backupRecord["meta"]!["floor"]! == 7, "each save keeps the previous good record as backup");
        var viaDelegates = new Dictionary<string, string>();
        var delegateSlots = Slots(new OriginalDelegateSaveStorage(key => viaDelegates.GetValueOrDefault(key, ""), (key, value) => viaDelegates[key] = value, () => { }, key => viaDelegates.Remove(key)));
        delegateSlots.Save(0, snapshot, 1); delegateSlots.Delete(0);
        Check(delegateSlots.List()[0].State == OriginalSaveSlotState.Empty && viaDelegates.Count == 0, "PlayerPrefs-style delegate storage plugs into the same slots");
    }

    // ---- verified write failure recovery -------------------------------------------
    {
        var inner = new OriginalMemorySaveStorage(); var faulty = new FaultyStorage(inner); var slots = Slots(faulty);
        slots.Save(0, Snapshot(2), 10); var good = inner.Read(slots.SlotKey(0));
        faulty.BreakKey = slots.SlotKey(0);
        Check(!slots.Save(0, Snapshot(3), 20), "a write that does not read back reports failure");
        Check(inner.Read(slots.SlotKey(0)) == good && inner.Read(slots.SlotKey(0) + ".backup") == good, "the previous good record is restored and kept as backup");
        faulty.BreakKey = null;
        var after = slots.Load(0, null, out var meta, out var recovered);
        Check((int)after["run"]!["floor"]! == 2 && meta.PlaytimeSeconds == 10 && !recovered, "load after a failed write returns the previous good snapshot");
        Check(slots.Save(0, Snapshot(4), 30) && (int)slots.Load(0, null, out _, out _)["run"]!["floor"]! == 4, "saving works again once storage recovers");
        faulty.BreakKey = slots.SlotKey(1);
        Check(!slots.Save(1, Snapshot(1), 5) && slots.List()[1].State == OriginalSaveSlotState.Empty, "a failed first write leaves the slot empty, not half-written");
        faulty.BreakKey = null; faulty.DropKey = slots.SlotKey(0);
        Check(!slots.Save(0, Snapshot(5), 40) && (int)slots.Load(0, null, out _, out _)["run"]!["floor"]! == 4, "a silently dropped write is detected by read-back");
    }

    // ---- delete and copy -------------------------------------------------------------
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        slots.Save(0, Snapshot(3, "run-c"), 100);
        Check(slots.Copy(0, 2), "copy to an empty slot verifies");
        var copied = slots.Load(2, null, out var copyMeta, out _); slots.Load(0, null, out var sourceMeta, out _);
        Check(Bytes(copied) == Bytes(slots.Load(0, null, out _, out _)) && Bytes(copyMeta.ToJson()) == Bytes(sourceMeta.ToJson()), "copy duplicates snapshot and metadata exactly");
        slots.Save(1, Snapshot(9, "run-d"), 5);
        Throws<InvalidOperationException>(() => slots.Copy(0, 1), "copy refuses to overwrite an occupied slot by default");
        Check(slots.Copy(0, 1, overwrite: true), "copy with overwrite replaces the target");
        var targetBackup = Parse(Data(storage.Read(slots.SlotKey(1) + ".backup")!));
        Check((string)targetBackup["meta"]!["runId"]! == "run-d" && (string)slots.Load(1, null, out _, out _)["run"]!["runId"]! == "run-c", "an overwritten slot keeps its previous run as backup");
        Throws<ArgumentException>(() => slots.Copy(1, 1), "copy to the same slot is refused");
        slots.Delete(2);
        Check(slots.List()[2].State == OriginalSaveSlotState.Empty && !storage.Values.ContainsKey(slots.SlotKey(2)) && !storage.Values.ContainsKey(slots.SlotKey(2) + ".backup"), "delete removes the slot and its backup");
        Check(slots.List()[0].State == OriginalSaveSlotState.Ready && slots.List()[1].State == OriginalSaveSlotState.Ready, "delete leaves other slots alone");
        Throws<ArgumentException>(() => slots.Copy(2, 0, overwrite: true), "copying from an empty slot is refused");
        Throws<ArgumentException>(() => slots.Load(2, null, out _, out _), "loading an empty slot is refused");
    }

    // ---- profile result archive ------------------------------------------------------
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        var profile = slots.LoadProfile(catalog, out var recovered);
        Check(profile.ResultArchive().Count == 0 && !recovered && !storage.Values.ContainsKey(slots.ProfileKey), "a missing profile starts empty without writing");
        for (var i = 0; i < 25; i++)
        {
            var run = (JObject)Snapshot(i, "run-" + i)["run"]!;
            var receipt = slots.RecordResult(profile, run, i % 5 == 0);
            if (i == 0) Check((bool)receipt["saved"]! && !(bool)receipt["duplicate"]!, "recording a result persists the profile with a verified write");
        }
        var archive = profile.ResultArchive();
        Check(archive.Count == OriginalProfile.ResultArchiveLimit && archive.Count == 20, "result archive is capped at 20");
        Check((string)archive[0]["key"]! == "run-5" && (string)archive[19]["key"]! == "run-24" && (int)archive[0]["result"]!["floor"]! == 5 && (int)archive[19]["result"]!["floor"]! == 24, "archive is FIFO and keyed by run ID");
        Check((int)profile.Snapshot()["progress"]!["runs"]! == 25, "progress outlives the archive cap");
        var duplicate = slots.RecordResult(profile, (JObject)Snapshot(24, "run-24")["run"]!, false);
        Check((bool)duplicate["duplicate"]! && profile.ResultArchive().Count == 20, "recording the same run twice is idempotent");
        var reloaded = new OriginalSaveSlots(storage, "web", "0.0.14.0", Now).LoadProfile(catalog, out _);
        Check(Bytes(reloaded.Snapshot()) == Bytes(profile.Snapshot()) && Bytes(reloaded.ResultArchive()) == Bytes(archive), "profile and its archive survive a reload");
        Check(storage.Read("AshenSpire.Unity.Profile.v1.web") != null, "the profile keeps its existing channel key");
        var faulty = new FaultyStorage(storage) { BreakKey = slots.ProfileKey }; var faultySlots = Slots(faulty);
        var receiptFail = faultySlots.RecordResult(reloaded, (JObject)Snapshot(1, "run-99")["run"]!, true);
        Check(!(bool)receiptFail["saved"]! && Bytes(slots.LoadProfile(catalog, out _).Snapshot()) == Bytes(profile.Snapshot()), "a failed profile write keeps the last good profile on disk");
    }

    // ---- corrupted data ----------------------------------------------------------------
    {
        var storage = new OriginalMemorySaveStorage(); var slots = Slots(storage);
        slots.Save(0, Snapshot(1), 1); slots.Save(0, Snapshot(2), 2);
        storage.Write(slots.SlotKey(0), "{\"schemaVersion\":1,\"data\":\"{}\",\"sha256\":\"tampered\"}");
        Check(slots.List()[0].State == OriginalSaveSlotState.RecoveredBackup && slots.List()[0].Meta!.Floor == 1, "list falls back to the backup when the primary checksum fails");
        var recoveredSnapshot = slots.Load(0, null, out _, out var recovered);
        Check(recovered && (int)recoveredSnapshot["run"]!["floor"]! == 1 && storage.Read(slots.SlotKey(0))!.Contains("tampered"), "load recovers the backup without deleting the corrupt primary");
        slots.Save(0, Snapshot(3), 3);
        Check(storage.Read(slots.SlotKey(0) + ".corrupt")!.Contains("tampered") && (int)Parse(Data(storage.Read(slots.SlotKey(0) + ".backup")!))["meta"]!["floor"]! == 1, "saving over a corrupt primary quarantines it and never replaces the good backup");
        foreach (var bad in new[] { "", "not json", "{\"schemaVersion\":1,\"data\":\"{}\"", "{\"schemaVersion\":1,\"data\":\"{}\",\"sha256\":\"x\"} trailing", "[1,2,3]" })
        {
            var s = new OriginalMemorySaveStorage(); var slotsBad = Slots(s); s.Write(slotsBad.SlotKey(1), bad);
            var state = slotsBad.List()[1].State;
            Check(bad.Length == 0 ? state == OriginalSaveSlotState.Empty : state == OriginalSaveSlotState.Corrupt, "malformed bytes are reported, not parsed: " + (bad.Length == 0 ? "(empty)" : bad));
        }
        var both = new OriginalMemorySaveStorage(); var bothSlots = Slots(both);
        both.Write(bothSlots.SlotKey(2), "garbage-a"); both.Write(bothSlots.SlotKey(2) + ".backup", "garbage-b");
        Throws<ArgumentException>(() => bothSlots.Load(2, null, out _, out _), "a slot with no readable record refuses to load");
        Check(both.Read(bothSlots.SlotKey(2)) == "garbage-a" && both.Read(bothSlots.SlotKey(2) + ".backup") == "garbage-b", "refused records are preserved byte-for-byte");
        var wrongShape = new OriginalMemorySaveStorage(); var wrongSlots = Slots(wrongShape);
        wrongShape.Write(wrongSlots.SlotKey(0), OriginalSaveJournal.Envelope(new JObject { ["schemaVersion"] = 1, ["meta"] = new JObject { ["act"] = -1 }, ["snapshot"] = new JObject() }));
        wrongShape.Write(wrongSlots.SlotKey(1), OriginalSaveJournal.Envelope(new JObject { ["schemaVersion"] = 99, ["meta"] = new JObject(), ["snapshot"] = new JObject() }));
        Check(wrongSlots.List()[0].State == OriginalSaveSlotState.Corrupt && wrongSlots.List()[1].State == OriginalSaveSlotState.Corrupt, "checksummed records with invalid metadata or unknown version are refused");
        var semantic = new OriginalMemorySaveStorage(); var semanticSlots = Slots(semantic);
        semanticSlots.Save(1, Snapshot(1), 1); semanticSlots.Save(1, Snapshot(2), 2);
        var restored = semanticSlots.Load(1, value => { if ((int)value["run"]!["floor"]! == 2) throw new ArgumentException("rejected by restore"); }, out _, out var semanticRecovered);
        Check(semanticRecovered && (int)restored["run"]!["floor"]! == 1, "a record the restore path rejects falls back to the backup");
        var profileStore = new OriginalMemorySaveStorage(); var profileSlots = Slots(profileStore);
        profileStore.Write(profileSlots.ProfileKey, "corrupt-profile");
        Throws<ArgumentException>(() => profileSlots.LoadProfile(catalog, out _), "an unreadable profile is a named failure, never a silent fresh profile");
        Check(profileStore.Read(profileSlots.ProfileKey) == "corrupt-profile", "the unreadable profile bytes are preserved");
    }

    Console.WriteLine($"SaveSlots: {passed} checks passed");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Console.WriteLine("SaveSlots: FAILED");
    return 1;
}

// Storage that corrupts the next write to BreakKey (one-shot) or silently ignores every write to DropKey.
internal sealed class FaultyStorage : IOriginalSaveStorage
{
    private readonly IOriginalSaveStorage _inner;
    public string BreakKey; public string DropKey;
    public FaultyStorage(IOriginalSaveStorage inner) => _inner = inner;
    public string Read(string key) => _inner.Read(key);
    public void Write(string key, string value)
    {
        if (key == DropKey) return;
        if (key == BreakKey) { BreakKey = null; _inner.Write(key, value.Substring(0, value.Length / 2)); return; }
        _inner.Write(key, value);
    }
    public void Delete(string key) => _inner.Delete(key);
    public void Flush() => _inner.Flush();
}
