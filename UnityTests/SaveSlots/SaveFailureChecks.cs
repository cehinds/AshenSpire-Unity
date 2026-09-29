// Storage exceptions are distinct from silent truncation: writes, flushes and
// read-back can each fail, including while rolling back. Never touch real saves.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class SaveFailureChecks
{
    public static void Run(OriginalContentCatalog catalog, Action<bool,string> check)
    {
        foreach (var mode in new[] { "read-before", "backup-before", "write-before", "write-after", "flush", "read-back", "persistent-after" })
        {
            var memory = new OriginalMemorySaveStorage();
            var storage = new ThrowingStorage(memory, "journal");
            var journal = new OriginalSaveJournal("journal", storage);
            journal.Save(new JObject { ["value"] = 1 }); journal.Save(new JObject { ["value"] = 2 });
            storage.Arm(mode);
            check(!journal.Save(new JObject { ["value"] = 3 }), "exception reports failed save: " + mode);
            storage.Arm(null);
            var restored = journal.Load(_ => { }, out var recovered);
            check((int)restored["value"]! == 2, "previous checkpoint survives storage exception: " + mode);
            if (mode == "persistent-after") check(recovered, "failed rollback recovers the untouched valid backup");
            check(journal.Save(new JObject { ["value"] = 4 }) && (int)journal.Load(_ => { }, out _)["value"]! == 4, "save retries successfully after storage recovers: " + mode);
        }
        foreach (var mode in new[] { "write-before", "write-after", "flush", "read-back" })
        {
            var memory = new OriginalMemorySaveStorage(); var storage = new ThrowingStorage(memory, "first");
            var journal = new OriginalSaveJournal("first", storage); storage.Arm(mode);
            check(!journal.Save(new JObject { ["value"] = 1 }) && !journal.HasSave, "failed first save leaves no partial record: " + mode);
        }
        {
            var memory = new OriginalMemorySaveStorage(); var storage = new ThrowingStorage(memory, "AshenSpire.Unity.Profile.v1.test");
            var slots = new OriginalSaveSlots(storage, "test", "0.0.25.0");
            var profile = slots.LoadProfile(catalog, out _); slots.SaveProfile(profile);
            var run = new JObject { ["runId"] = "retry-result", ["classId"] = (string)catalog.Table("classes").First()["id"], ["actNumber"] = 1, ["floor"] = 3,
                ["stats"] = new JObject { ["fightsWon"] = 1, ["damageDealt"] = 10, ["damageTaken"] = 2 } };
            storage.Arm("write-before"); var first = slots.RecordResult(profile, run, false);
            check(!(bool)first["saved"]! && !(bool)first["duplicate"]!, "finished run reports a failed profile write");
            check(slots.LoadProfile(catalog, out _).ResultArchive().Count == 0, "failed result did not overwrite the durable profile");
            storage.Arm("write-before"); var second = slots.RecordResult(profile, run, false);
            check(!(bool)second["saved"]! && (bool)second["duplicate"]!, "duplicate result retries persistence and does not falsely report success");
            storage.Arm(null); var third = slots.RecordResult(profile, run, false);
            check((bool)third["saved"]! && (bool)third["duplicate"]!, "duplicate result is saved when storage becomes available");
            var saved = slots.LoadProfile(catalog, out _);
            check(saved.ResultArchive().Count == 1 && (int)saved.Snapshot()["progress"]!["runs"]! == 1, "retried result persists exactly once");
        }
        {
            var memory = new OriginalMemorySaveStorage(); var slots = new OriginalSaveSlots(memory, "recovery", "0.0.25.0");
            var original = new OriginalProfile(catalog); var backup = OriginalSaveJournal.Envelope(original.Snapshot());
            var incompatible = OriginalSaveJournal.Envelope(new JObject { ["schemaVersion"] = 999, ["futureData"] = "keep these bytes" });
            memory.Write(slots.ProfileKey, incompatible); memory.Write(slots.ProfileKey + ".backup", backup);
            var loaded = slots.LoadProfile(catalog, out var recovered);
            check(recovered, "profile schema rejection recovers the valid backup");
            check(slots.SaveProfile(loaded), "recovered profile can save through the same journal");
            check(memory.Read(slots.ProfileKey + ".backup") == backup, "recovered profile save preserves the surviving backup");
            check(memory.Read(slots.ProfileKey + ".corrupt") == incompatible, "unsupported primary profile is quarantined byte-for-byte even with a valid checksum");
        }
    }
}

internal sealed class ThrowingStorage : IOriginalSaveStorage
{
    private readonly IOriginalSaveStorage _inner; private readonly string _target;
    private string _mode; private bool _armed, _written;
    public ThrowingStorage(IOriginalSaveStorage inner, string target) { _inner = inner; _target = target; }
    public void Arm(string mode) { _mode = mode; _armed = mode != null; _written = false; }
    private void Fail() { if (_mode != "persistent-after") _armed = false; throw new IOException("Simulated unavailable storage"); }
    public string Read(string key)
    {
        if (_armed && key == _target && (_mode == "read-before" || (_mode == "read-back" && _written))) Fail();
        return _inner.Read(key);
    }
    public void Write(string key, string value)
    {
        if (_armed && _mode == "backup-before" && key == _target + ".backup") Fail();
        if (_armed && key == _target && _mode == "write-before") Fail();
        if (key == _target) _written = true;
        if (_armed && key == _target && (_mode == "write-after" || _mode == "persistent-after"))
        { _inner.Write(key, value.Substring(0, value.Length / 2)); Fail(); }
        _inner.Write(key, value);
    }
    public void Delete(string key) => _inner.Delete(key);
    public void Flush() { if (_armed && _mode == "flush") Fail(); _inner.Flush(); }
}
