// Unity-compiled controller/view fixtures with isolated storage. Never reads or
// writes real saved games; this does not claim physical input or Web quota tests.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AshenSpire.Application;
using AshenSpire.Domain;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Editor
{
    public static class ProfileRecoveryValidation
    {
        public static void Run()
        {
            var checks = new JArray();
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException("Profile recovery fixture failed: " + label);
                checks.Add(label);
            }
            var catalog = new OriginalContentCatalog(Resources.Load<TextAsset>("Original/content").text);
            var content = JsonUtility.FromJson<CampaignDefinition>(Resources.Load<TextAsset>("campaign").text);
            var memory = new OriginalMemorySaveStorage();
            var storage = new FaultStorage(memory);
            var slots = new OriginalSaveSlots(storage, "isolated-fixture", "0.0.25.0");
            var root = new VisualElement();
            var view = new CampaignView(root, false, true, false, true);
            var host = new GameObject("IsolatedProfileRecoveryFixture"); host.SetActive(false);
            var controller = host.AddComponent<RunController>(); // Inactive: no OnEnable, PlayerPrefs or network.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object value) => typeof(RunController).GetField(name, flags).SetValue(controller, value);
            object Get(string name) => typeof(RunController).GetField(name, flags).GetValue(controller);
            object Invoke(string name, params object[] args) => typeof(RunController).GetMethods(flags)
                .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(controller, args);
            bool Message(string text) => root.Query<Label>().ToList().Any(label => label.text.Contains(text));
            Set("_content", content); Set("_originalContent", catalog); Set("_slotSaves", slots); Set("_view", view);
            try
            {
                memory.Write(slots.ProfileKey, "damaged-primary"); memory.Write(slots.ProfileKey + ".backup", "damaged-backup");
                foreach (var action in new[] { "CreateOriginal", "ShowOriginalProfile", "OpenCoop", "ResumeSlot", "ShowSaveSlots" })
                {
                    if (action == "CreateOriginal" || action == "ResumeSlot") Invoke(action, 0); else Invoke(action);
                    Check(Message("Your profile could not be opened"), action + " shows a useful profile error");
                    Check(Get("_profile") == null, action + " does not replace the damaged profile with a new one");
                    Check(memory.Read(slots.ProfileKey) == "damaged-primary" && memory.Read(slots.ProfileKey + ".backup") == "damaged-backup", action + " preserves both records byte-for-byte");
                }
                var original = new OriginalProfile(catalog);
                var good = OriginalSaveJournal.Envelope(original.Snapshot());
                var future = OriginalSaveJournal.Envelope(new JObject { ["schemaVersion"] = 999, ["futureData"] = "preserve" });
                memory.Write(slots.ProfileKey, future); memory.Write(slots.ProfileKey + ".backup", good);
                Invoke("ShowOriginalProfile");
                Check(Message("Recovered your previous profile backup"), "recovered backup has a visible warning");
                Check(Get("_profile") is OriginalProfile, "recovery installs the valid profile");
                Check((bool)Invoke("SaveOriginalProfile"), "recovered profile can be saved by the controller");
                Check(memory.Read(slots.ProfileKey + ".backup") == good, "controller uses the same recovery journal and preserves the valid backup");
                Check(memory.Read(slots.ProfileKey + ".corrupt") == future, "controller quarantines the incompatible primary exactly");
                Invoke("ShowOriginalProfile");
                Check(Message("Recovered your previous profile backup"), "successful autosave does not hide the recovery warning");
                var loaded = (OriginalProfile)Get("_profile");
                var run = new JObject { ["runId"] = "profile-ui-retry", ["classId"] = (string)catalog.Table("classes").First()["id"], ["actNumber"] = 1, ["floor"] = 3,
                    ["stats"] = new JObject { ["fightsWon"] = 1, ["damageDealt"] = 10, ["damageTaken"] = 2 } };
                storage.Fail = true;
                var receipt = (JObject)Invoke("RecordOriginalResult", run, false);
                Check(!(bool)receipt["saved"], "controller reports result persistence failure");
                Check(loaded.ResultArchive().Count == 1, "failed write retains the result in memory");
                Invoke("ShowOriginalProfile");
                Check(Message("Your progress could not be saved"), "failed profile write is visible on the profile screen");
                Check(root.Q<Label>("persistence-notice") != null, "storage warning is a named UI element");
                Check(!(bool)Invoke("SaveOriginalProfile") && (bool)Get("_profileWritePending"), "continued storage failure remains pending");
                receipt = (JObject)Invoke("RecordOriginalResult", run, false);
                Check(!(bool)receipt["saved"] && (bool)receipt["duplicate"], "duplicate result cannot falsely report a successful save");
                storage.Fail = false;
                Check((bool)Invoke("SaveOriginalProfile") && !(bool)Get("_profileWritePending"), "controller retries successfully when storage returns");
                Check(slots.LoadProfile(catalog, out _).ResultArchive().Count == 1, "retry persists the result exactly once");
                Invoke("ShowOriginalProfile");
                Check(root.Q<Label>("persistence-notice") == null, "successful retry clears the failure warning");
                view.PersistenceNotice("Temporary warning"); var existing = root.Q<Label>("persistence-notice");
                view.PersistenceNotice("Updated warning");
                Check(ReferenceEquals(existing, root.Q<Label>("persistence-notice")), "warning updates keep the existing view and control focus");
                view.PersistenceNotice(null);
                Check(root.Q<Label>("persistence-notice") == null, "resolved warning is removed without rebuilding the view");
                Set("_mapViewerSettings", new JObject { ["solo"] = new JObject { ["zoom"] = 1.1 } }); Set("_mapViewerDirty", true);
                storage.Fail = true; Invoke("FlushMapView");
                Check((bool)Get("_mapViewerDirty") && (bool)Get("_profileWritePending"), "failed map preference save remains dirty for retry");
                Check(Message("Your progress could not be saved"), "debounced save failure appears immediately on the existing screen");
                storage.Fail = false; Invoke("FlushMapView");
                Check(!(bool)Get("_mapViewerDirty") && !(bool)Get("_profileWritePending"), "map preferences clear dirty state only after verified retry");
                Check(root.Q<Label>("persistence-notice") == null, "verified debounced retry removes its warning");
            }
            finally { Set("_profile", null); view.Dispose(); UnityEngine.Object.DestroyImmediate(host); }
            var output = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../Builds/profile-recovery-fixtures.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, new JObject { ["passed"] = true, ["checks"] = checks, ["physicalInput"] = false,
                ["scope"] = "Unity-compiled controller/view callbacks with isolated failing storage; no real user saves, physical input or browser quota simulation." }.ToString());
            Debug.Log("Profile recovery fixtures: " + checks.Count + " checks passed");
        }
        private sealed class FaultStorage : IOriginalSaveStorage
        {
            private readonly OriginalMemorySaveStorage _memory;
            public bool Fail;
            public FaultStorage(OriginalMemorySaveStorage memory) { _memory = memory; }
            public string Read(string key) => _memory.Read(key);
            public void Write(string key, string value) { if (Fail) throw new IOException("Fixture storage unavailable"); _memory.Write(key, value); }
            public void Delete(string key) { if (Fail) throw new IOException("Fixture storage unavailable"); _memory.Delete(key); }
            public void Flush() { if (Fail) throw new IOException("Fixture storage unavailable"); _memory.Flush(); }
        }
    }
}
