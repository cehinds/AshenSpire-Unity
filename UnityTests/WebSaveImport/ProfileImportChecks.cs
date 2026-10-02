#nullable enable
// Original-game PROFILE import checks against fixtures written by the real JavaScript
// save manager (export-profile-reference.mjs → profile-reference.json + receipt).
using System.Security.Cryptography;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class ProfileImportChecks
{
    public static int Run(string root, OriginalContentCatalog catalog)
    {
        var checks = 0;
        void Check(bool yes, string label) { if (!yes) throw new Exception("profile import: " + label); checks++; }
        void Refuse(Action action, string label, string? contains = null)
        {
            string? message = null;
            try { action(); } catch (ArgumentException e) { message = e.Message; } catch (Newtonsoft.Json.JsonException e) { message = e.Message; }
            Check(message != null && (contains == null || message.Contains(contains)), label + (message == null ? " (accepted)" : " (" + message + ")"));
        }
        var folder = Path.Combine(root, "UnityTests/WebSaveImport");
        var bytes = File.ReadAllBytes(Path.Combine(folder, "profile-reference.json"));
        var receipt = JObject.Parse(File.ReadAllText(Path.Combine(folder, "profile-reference.receipt.json")));
        Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == (string)receipt["outputSha256"], "fixture bytes match their receipt");
        var fixtures = (JObject)JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes))["fixtures"]!;
        string Meta(string name) => (string)fixtures[name]!["meta"]!;
        JObject Loaded(string name) => (JObject)fixtures[name]!["loaded"]!;
        HashSet<string> Set(JToken? rows) => new((rows as JArray ?? new JArray()).Values<string>().Select(x => x!));
        OriginalWebProfileImportResult Merge(string text, OriginalProfile? existing = null, OriginalPlayerSettings? settings = null) => OriginalWebProfileImport.Merge(text, catalog, existing, settings);
        JObject Strip(JObject snapshot) { var copy = (JObject)snapshot.DeepClone(); copy.Remove(OriginalWebProfileImport.ReceiptKey); return copy; }

        // ---- fresh profile ---------------------------------------------------------
        var empty = new OriginalProfile(catalog);
        var fresh = Merge(Meta("fresh"), empty);
        Check(!fresh.Changed && fresh.SourceSchemaVersion == 2 && !fresh.Migrated, "fresh original profile changes nothing");
        Check(JToken.DeepEquals(fresh.Profile.Snapshot(), empty.Snapshot()), "fresh import adds no import record");
        Check(OriginalWebProfileImport.IsProfile(Meta("fresh")) && OriginalWebProfileImport.IsProfile((string)fixtures["fresh"]!["exported"]!), "raw and exported profiles are recognised");

        // ---- history: counts, unlock mapping and discoveries match the JavaScript oracle
        var history = Merge(Meta("history"), new OriginalProfile(catalog));
        var h = history.Profile.Snapshot(); var js = Loaded("history");
        Check(history.Changed && history.SourceResults == 5 && history.ImportedResults == 5 && history.DuplicateResults == 0, "five original results imported");
        Check(((JArray)h["results"]!).Count == 5 && ((JArray)h["completedRunIds"]!).Count == 5, "results and keys stored together");
        for (var i = 0; i < 5; i++)
        {
            var row = (JObject)h["results"]![i]!; var original = (JObject)js["results"]![i]!;
            foreach (var key in new[] { "victory", "seed", "class", "className", "act", "floor", "fightsWon", "damageDealt", "damageTaken", "custom", "ascension", "bosses", "name" })
                Check(JToken.DeepEquals(row[key], original[key]), "result " + i + " keeps " + key);
            Check((string?)row["origin"] == "original-web", "result " + i + " marked as imported");
        }
        Check(history.Profile.ResultArchive().All(r => ((string?)r["key"])?.StartsWith(OriginalWebProfileImport.ResultKeyPrefix) == true), "every imported result is keyed");
        foreach (var key in new[] { "runs", "wins", "maxAct" }) Check(JToken.DeepEquals(h["progress"]![key], js["progress"]![key]), "progress " + key + " matches original");
        Check(Set(h["progress"]!["bosses"]).SetEquals(Set(js["progress"]!["bosses"])) && Set(h["progress"]!["wonClasses"]).SetEquals(Set(js["progress"]!["wonClasses"])), "progress bosses and classes match original");
        Check(Set(h["unlocked"]).SetEquals(Set(js["unlocked"])) && history.AddedUnlocks.Count == 15 && history.EarnedUnlocks.Count == 0, "unlocks map one-to-one with the original");
        Check(history.UnknownUnlocks.Count == 0 && history.UnknownArmaments.Count == 0, "baseline content IDs are all known");
        Check(Set(h["found"]).SetEquals(new[] { "dagger", "katana", "halberd" }) && Set(h["discoveredArmaments"]).SetEquals(new[] { "dagger", "katana" }), "wardrobe and kit ledger kept apart");
        Check(JToken.DeepEquals(new JArray(((JArray)h["discoveryReceipts"]!).Select(r => { var c = (JObject)r.DeepClone(); Check((string?)c["origin"] == "original-web", "receipt marked as imported"); c.Remove("origin"); return c; })), js["discoveryReceipts"]), "discovery receipts preserved exactly");
        var telemetry = history.Profile.Telemetry();
        Check((int)telemetry["runs"]! == 4 && (int)telemetry["wins"]! == 2 && (int)telemetry["customCount"]! == 1, "telemetry reads imported history");
        Check(JToken.DeepEquals(OriginalProfile.Restore(catalog, h).Snapshot(), h), "merged profile round-trips through Restore");
        var memory = new OriginalMemorySaveStorage(); var slots = new OriginalSaveSlots(memory, "profile-import", "test");
        Check(slots.SaveProfile(history.Profile), "merged profile persists through the journal");
        Check(JToken.DeepEquals(slots.LoadProfile(catalog, out _).Snapshot(), h), "persisted merged profile reloads exactly");

        // ---- dedupe on re-import, in every wrapper ------------------------------------
        var exported = (string)fixtures["history"]!["exported"]!;
        var archive = new JObject { ["exportedAt"] = "2026-10-02", ["game"] = "Ashen Spire", ["archive"] = new JObject { ["id"] = "a1", ["kind"] = "meta", ["save"] = Meta("history") } }.ToString();
        var reordered = new JObject(JObject.Parse(Meta("history")).Properties().Reverse().Select(p => new JProperty(p.Name, p.Value.DeepClone()))).ToString();
        foreach (var (label, text) in new[] { ("raw", Meta("history")), ("export file", exported), ("meta archive", archive), ("reordered JSON", reordered) })
        {
            var again = Merge(text, history.Profile);
            Check(!again.Changed && again.DuplicateResults == 5 && again.ImportedResults == 0 && again.AddedRuns == 0, "re-import adds nothing: " + label);
            Check(JToken.DeepEquals(again.Profile.Snapshot(), h), "re-import leaves profile identical: " + label);
        }
        Check(OriginalWebProfileImport.IsProfile(archive), "meta archive recognised");

        // ---- a later export of the same player adds only what is new -----------------------
        var later = Merge(Meta("historyLater"), history.Profile); var l = later.Profile.Snapshot(); var jsLater = Loaded("historyLater");
        Check(later.Changed && later.ImportedResults == 3 && later.DuplicateResults == 5 && later.AddedRuns == 3 && later.AddedWins == 1, "later export adds three runs");
        foreach (var key in new[] { "runs", "wins", "maxAct" }) Check(JToken.DeepEquals(l["progress"]![key], jsLater["progress"]![key]), "later progress " + key + " matches original");
        Check(Set(l["unlocked"]).SetEquals(Set(jsLater["unlocked"])) && Set(l["found"]).SetEquals(Set(jsLater["found"])), "later unlocks and wardrobe match original");
        var direct = Merge(Meta("historyLater"), new OriginalProfile(catalog)).Profile.Snapshot();
        Check(JToken.DeepEquals(Strip(direct), Strip(l)), "sequential imports equal one import of the later export (order, keys, progress)");
        Check(((JArray)l["results"]!).Select(r => (string?)r["seed"]).SequenceEqual(((JArray)jsLater["results"]!).Select(r => (string?)r["seed"])), "history order follows the original");

        // ---- veteran: 20 results, 25 counted runs -----------------------------------------
        var veteran = Merge(Meta("veteran")).Profile.Snapshot();
        Check(((JArray)veteran["results"]!).Count == 20 && (int)veteran["progress"]!["runs"]! == 25 && (int)veteran["progress"]!["wins"]! == 9, "original tally outlives its 20-result history");
        Check(Set(veteran["unlocked"]).SetEquals(Set(Loaded("veteran")["unlocked"])), "veteran unlocks match original");

        // ---- existing Unity profile is preserved -------------------------------------------
        var native = new OriginalProfile(catalog);
        native.SetSettings(new JObject { ["mapViewer"] = new JObject { ["zoom"] = 1.25 } });
        JObject Run(string seed, string cls, int act, bool boss) => new JObject { ["classId"] = cls, ["seedString"] = seed, ["actNumber"] = act, ["floor"] = 4 * act, ["stats"] = new JObject { ["fightsWon"] = 3, ["damageDealt"] = 30, ["damageTaken"] = 10 }, ["bossesBeaten"] = boss ? new JArray("stitchedKing") : new JArray() };
        for (var i = 0; i < 18; i++) native.Finish("native-" + i, Run("N" + i, i % 2 == 0 ? "rogue" : "reaver", 1 + i % 2, i % 2 == 1), i == 3);
        var nativeBefore = native.Snapshot(); var nativeArchive = native.ResultArchive();
        var merged = Merge(Meta("history"), native); var m = merged.Profile.Snapshot();
        Check(JToken.DeepEquals(native.Snapshot(), nativeBefore), "merge never mutates the existing profile");
        Check(JToken.DeepEquals(m["settings"], nativeBefore["settings"]), "Unity profile settings bag untouched");
        var mergedArchive = merged.Profile.ResultArchive();
        Check(((JArray)m["results"]!).Count == 20 && merged.ArchivedOnlyResults == 3, "history cap keeps every native result and the two newest imported");
        Check(JToken.DeepEquals(new JArray(mergedArchive.Skip(2)), nativeArchive), "native results keep their keys and order as newest");
        Check(mergedArchive.Take(2).All(r => ((string?)r["key"])!.StartsWith(OriginalWebProfileImport.ResultKeyPrefix)), "imported results keyed in front");
        Check(((JArray)nativeBefore["completedRunIds"]!).Values<string>().All(id => ((JArray)m["completedRunIds"]!).Values<string>().Contains(id)), "native completed run IDs kept");
        Check((int)m["progress"]!["runs"]! == 23 && (int)m["progress"]!["wins"]! == 3, "native and original tallies add");
        Check(Set(nativeBefore["unlocked"]).IsSubsetOf(Set(m["unlocked"])), "native unlocks kept");
        Check(merged.EarnedUnlocks.All(id => !Set(nativeBefore["unlocked"]).Contains(id)), "only genuinely new unlocks reported");
        var mergedAgain = Merge(Meta("history"), merged.Profile);
        Check(!mergedAgain.Changed && mergedAgain.DuplicateResults == 5, "evicted imported results still deduplicate");
        var nativeAfter = merged.Profile; nativeAfter.Finish("native-after", Run("AFTER", "herald", 1, false), false);
        Check((string?)nativeAfter.ResultArchive().Last()["key"] == "native-after" && (int)nativeAfter.Snapshot()["progress"]!["runs"]! == 24, "native play continues on the merged profile");

        // ---- schema versions -------------------------------------------------------------
        var before = merged.Profile.Snapshot();
        Refuse(() => Merge(Meta("newer"), merged.Profile), "newer original schema refused", "schema version 3");
        Refuse(() => Merge((string)fixtures["newer"]!["exported"]!, merged.Profile), "newer schema refused inside an export file", "newer than this importer supports");
        foreach (var bad in new JToken[] { new JValue("2"), new JValue(-1), new JValue(2.5), new JObject() })
        { var edit = JObject.Parse(Meta("history")); edit["schemaVersion"] = bad; Refuse(() => Merge(edit.ToString(), merged.Profile), "unreadable schema version refused: " + bad.ToString(Newtonsoft.Json.Formatting.None), "unreadable schema version"); }
        Check(JToken.DeepEquals(merged.Profile.Snapshot(), before), "refusals leave the Unity profile unchanged");
        var schema1 = Merge(Meta("schema1"));
        Check(schema1.Migrated && schema1.SourceSchemaVersion == 1 && Set(schema1.Profile.Snapshot()["discoveredArmaments"]).SetEquals(Set(Loaded("schema1")["discoveredArmaments"])), "schema 1 migrates the kit ledger like the original");
        Check(Set(schema1.Profile.Snapshot()["unlocked"]).IsSupersetOf(Set(Loaded("schema1")["unlocked"])), "schema 1 unlocks kept");
        var schema0 = Merge(Meta("schema0"));
        Check(schema0.Migrated && schema0.SourceSchemaVersion == 0 && (int)schema0.Profile.Snapshot()["progress"]!["runs"]! == 1 && schema0.Notes.Any(), "unversioned profile rebuilds its tally from history");
        Check(schema0.Settings.Muted && schema0.MappedSettings.Count == 1, "unversioned profile settings map");

        // ---- unknown content and damaged input ----------------------------------------------
        var future = JObject.Parse(Meta("history"));
        ((JArray)future["unlocked"]!).Add("futureUnlock"); ((JArray)future["found"]!).Add("futureBlade"); future["seen"] = new JObject { ["cards"] = new JArray("strike") };
        var unknown = Merge(future.ToString());
        Check(unknown.UnknownUnlocks.SequenceEqual(new[] { "futureUnlock" }) && unknown.UnknownArmaments.SequenceEqual(new[] { "futureBlade" }), "unknown IDs reported");
        var kept = unknown.Profile.Snapshot()[OriginalWebProfileImport.ReceiptKey]!["preserved"]!;
        Check(Set(kept["unknownUnlocks"]).Contains("futureUnlock") && Set(kept["unknownArmaments"]).Contains("futureBlade") && kept["fields"]?["seen"] != null, "unknown IDs and original-only fields preserved");
        Check(!Set(unknown.Profile.Snapshot()["unlocked"]).Contains("futureUnlock") && unknown.UnmappedFields.Contains("seen"), "unknown IDs never granted");
        Refuse(() => Merge("{\"schemaVersion\":5,\"class\":\"reaver\",\"deck\":[]}"), "run save refused as a profile", "run save");
        Refuse(() => Merge(new JObject { ["archive"] = new JObject { ["kind"] = "run", ["save"] = "{}" } }.ToString()), "run archive refused as a profile", "run archive");
        Refuse(() => Merge("{\"results\":[],\"results\":[]}"), "duplicate JSON fields refused");
        Refuse(() => Merge(""), "empty input refused");
        var bad1 = JObject.Parse(Meta("history")); bad1["progress"]!["wins"] = 99; Refuse(() => Merge(bad1.ToString()), "wins above runs refused", "more wins than runs");
        var bad2 = JObject.Parse(Meta("history")); bad2["unlocked"] = "all"; Refuse(() => Merge(bad2.ToString()), "damaged unlock list refused", "unlocked");
        var bad3 = JObject.Parse(Meta("history")); bad3["results"] = new JObject(); Refuse(() => Merge(bad3.ToString()), "damaged history refused", "run history");
        var bad4 = JObject.Parse(Meta("history")); ((JArray)bad4["results"]!).Add("garbage"); ((JArray)bad4["results"]!).Add(new JObject { ["victory"] = "yes" });
        var skipped = Merge(bad4.ToString()); Check(skipped.SkippedResults == 2 && skipped.ImportedResults == 5, "unreadable result rows skipped and counted");
        var receiptFuture = merged.Profile.Snapshot(); receiptFuture[OriginalWebProfileImport.ReceiptKey]!["version"] = 99;
        Refuse(() => Merge(Meta("history"), OriginalProfile.Restore(catalog, receiptFuture)), "newer import record refused", "newer build");

        // ---- settings mapping --------------------------------------------------------------
        var device = new OriginalPlayerSettings { MasterVolume = .5, LoadContentMods = true, UiVolume = .4 };
        var deviceBefore = device.ToJson();
        var mapped = Merge(Meta("history"), null, device); var s = mapped.Settings;
        Check(JToken.DeepEquals(device.ToJson(), deviceBefore), "existing settings object untouched");
        Check(s.ReducedMotion && !s.ScreenShake && !s.Muted && s.AnimationSpeed == 2 && !s.InstantAnimations, "motion, shake, mute and pacing map");
        Check(Math.Abs(s.MusicVolume - .3) < 1e-9 && Math.Abs(s.SfxVolume - .6) < 1e-9, "volumes map from percent");
        Check(Math.Abs(s.UiScale - 1.2) < 1e-9 && Math.Abs(s.TextScale - 1.2) < 1e-9, "UI and text size map from original steps");
        Check(s.MasterVolume == .5 && s.LoadContentMods && s.UiVolume == .4, "Unity-only settings kept");
        Check(mapped.MappedSettings.Count == 8, "eight settings mapped");
        foreach (var name in new[] { "colorblindSafe", "musicEnabled", "highContrast", "mapMode", "holdConfirm", "seenTutorial" })
            Check(mapped.UnmappedSettings.Any(u => u.StartsWith(name + ":")), "unmapped setting listed: " + name);
        Check(JToken.DeepEquals(mapped.Profile.Snapshot()[OriginalWebProfileImport.ReceiptKey]!["preserved"]!["settings"], JObject.Parse(Meta("history"))["settings"]), "original settings preserved verbatim");
        var instant = JObject.Parse(Meta("history")); instant["settings"]!["animSpeed"] = "instant"; instant["settings"]!["uiScale"] = "Auto";
        var inst = Merge(instant.ToString(), null, device);
        Check(inst.Settings.InstantAnimations && inst.Settings.UiScale == device.UiScale && inst.UnmappedSettings.Any(u => u.StartsWith("uiScale:")), "instant pacing maps; Auto size keeps the Unity size");
        Console.WriteLine("Profile import: " + checks + " checks passed (" + string.Join(", ", mapped.UnmappedSettings.Select(u => u.Split(':')[0])) + " unmapped)");
        return checks;
    }
}
