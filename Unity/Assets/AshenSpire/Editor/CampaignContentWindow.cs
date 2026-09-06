// CampaignContentWindow.cs — structured owner editor for the authoritative JSON file.
// OPEN: AshenSpire > Campaign Content Editor. Draft fields apply immediately in memory.
// UNDO: Unity Undo/Redo covers fields, duplicate/remove and reward enrollment.
// SAVE: validates definitions/art, checks the loaded source hash, backs up and imports.
// RECOVERY: closing/recompiling checkpoints to Builds/AuthoringDrafts; no game assets
// or runtime saves hold draft state. Keep existing IDs stable; duplicate to add content.
using System;
using System.IO;
using System.Linq;
using System.Text;
using AshenSpire.Domain;
using UnityEditor;
using UnityEngine;

namespace AshenSpire.Editor
{
    public sealed class CampaignContentWindow : EditorWindow
    {
        [SerializeField] private CampaignContentDraft _draft;
        [SerializeField] private string _sourceHash, _savedJson;
        [SerializeField] private int _table, _row;
        [SerializeField] private string _search = "";
        private SerializedObject _serialized;
        private string _notice;
        private bool _dirty, _showJson;
        private Vector2 _scroll;
        private static string Repository => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
        private static string Source => Path.Combine(Repository, "GameContent/Unity/campaign.json");
        private static string Recovery => Path.Combine(Repository, "Builds/AuthoringDrafts/campaign-draft.json");
        private static readonly string[] Choices = CampaignContentDraft.Tables.Concat(new[] { "Campaign settings" }).ToArray();
        private static readonly string[] SettingsFields = { "Energy", "HandSize", "RestHealing", "PotionHealing", "StartingPotions", "CommonRewardTag", "RewardCards", "Audio.Volume" };
        [MenuItem("AshenSpire/Campaign Content Editor")]
        public static void Open() => GetWindow<CampaignContentWindow>("Campaign Content");
        private void OnEnable()
        {
            minSize = new Vector2(440, 560);
            Undo.undoRedoPerformed += OnUndo;
            AssemblyReloadEvents.beforeAssemblyReload += Checkpoint;
            if (_draft == null) LoadDisk(true);
            else Bind();
        }
        private void Bind()
        {
            _serialized?.Dispose();
            _serialized = new SerializedObject(_draft);
            RefreshDirty();
        }
        private void LoadDisk(bool recover)
        {
            try
            {
                var bytes = File.ReadAllBytes(Source);
                var json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
                var content = JsonUtility.FromJson<CampaignDefinition>(json);
                if (content == null) throw new ArgumentException("The campaign JSON is empty.");
                if (_draft == null) { _draft = CreateInstance<CampaignContentDraft>(); _draft.hideFlags = HideFlags.HideAndDontSave; }
                Undo.ClearUndo(_draft);
                _draft.Content = content;
                _sourceHash = CampaignContentFile.Fingerprint(bytes);
                _savedJson = JsonUtility.ToJson(content);
                _notice = "Loaded authoritative campaign JSON.";
                if (recover && File.Exists(Recovery))
                {
                    var checkpoint = JsonUtility.FromJson<CampaignDraftCheckpoint>(File.ReadAllText(Recovery));
                    if (checkpoint == null || string.IsNullOrEmpty(checkpoint.SourceHash) || string.IsNullOrEmpty(checkpoint.DraftJson))
                        throw new ArgumentException("Draft checkpoint is unreadable; its original file is preserved.");
                    if (checkpoint.DraftJson != checkpoint.SavedJson)
                    {
                        _draft.Content = JsonUtility.FromJson<CampaignDefinition>(checkpoint.DraftJson);
                        _savedJson = checkpoint.SavedJson; _sourceHash = checkpoint.SourceHash;
                        _notice = "Recovered unsaved draft. If the source changed on disk, Save will reject the conflict.";
                    }
                }
                _row = 0; Bind();
            }
            catch (Exception error) { _notice = error.Message; if (_draft != null) Bind(); }
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Campaign content", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edit structured fields, then Save & Import. Switching records keeps your draft. Keep existing IDs stable; duplicate to add content. Unity Undo/Redo is available.", MessageType.Info);
            if (_draft?.Content == null || _serialized == null)
            {
                if (GUILayout.Button("Reload source")) LoadDisk(false);
                EditorGUILayout.HelpBox(_notice ?? "Could not load content.", MessageType.Error); return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Undo")) Undo.PerformUndo();
                if (GUILayout.Button("Redo")) Undo.PerformRedo();
                if (GUILayout.Button("Checkpoint draft")) Checkpoint();
            }
            _serialized.Update();
            var table = EditorGUILayout.Popup("Table", Mathf.Clamp(_table, 0, Choices.Length - 1), Choices);
            if (table != _table) { _table = table; _row = 0; _search = ""; }
            var settings = _table == CampaignContentDraft.Tables.Length;
            var tableName = settings ? null : Choices[_table];
            Array rows = settings ? null : _draft.Rows(tableName);
            var visible = true;
            if (!settings)
            {
                _search = EditorGUILayout.TextField("Search ID or name", _search);
                var matches = Enumerable.Range(0, rows.Length).Where(index => RowLabel(rows.GetValue(index)).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (matches.Length > 0)
                {
                    var selection = Array.IndexOf(matches, _row);
                    _row = matches[EditorGUILayout.Popup("Record", Math.Max(0, selection), matches.Select(index => RowLabel(rows.GetValue(index))).ToArray())];
                }
                else { visible = false; EditorGUILayout.HelpBox("No matching records. Clear the search or add a new record.", MessageType.Info); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New record")) Change("Add content record", () => { _row = _draft.Add(tableName); _search = ""; });
                    using (new EditorGUI.DisabledScope(!visible))
                    {
                        if (GUILayout.Button("Duplicate")) Change("Duplicate content record", () => { _row = _draft.Add(tableName, _row); _search = ""; });
                        if (GUILayout.Button("Remove row")) Change("Remove content record", () => { _draft.Remove(tableName, _row); _row = 0; });
                    }
                }
            }
            _serialized.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (settings)
            {
                foreach (var name in SettingsFields)
                {
                    var property = _serialized.FindProperty("Content." + name);
                    if (name == "Audio.Volume") property.floatValue = EditorGUILayout.Slider("Sound volume", property.floatValue, 0, 1);
                    else EditorGUILayout.PropertyField(property, true);
                }
            }
            else
            {
                var array = _serialized.FindProperty(CampaignContentDraft.PropertyPath(tableName));
                if (array != null && array.arraySize > 0 && visible)
                {
                    _row = Mathf.Clamp(_row, 0, array.arraySize - 1);
                    var record = array.GetArrayElementAtIndex(_row);
                    var child = record.Copy(); var end = record.GetEndProperty();
                    if (child.NextVisible(true))
                        do { if (SerializedProperty.EqualContents(child, end)) break; EditorGUILayout.PropertyField(child, true); } while (child.NextVisible(false));
                    DrawSprite(record, tableName == "Heroes");
                    if (tableName == "Cards" && GUILayout.Button("Add this card to reward catalog"))
                    {
                        _serialized.ApplyModifiedProperties();
                        var id = _draft.Content.Cards[_row].Id;
                        Change("Add reward card", () => _draft.AddReward(id));
                        _notice = "Reward catalog updated in the draft. Save validates class/shared affinity rules.";
                    }
                    _showJson = EditorGUILayout.Foldout(_showJson, "Read-only JSON preview", true);
                    if (_showJson) EditorGUILayout.SelectableLabel(JsonUtility.ToJson(_draft.Rows(tableName).GetValue(_row), true), EditorStyles.textArea, GUILayout.MinHeight(240));
                }
            }
            EditorGUILayout.EndScrollView();
            if (_serialized.ApplyModifiedProperties()) RefreshDirty();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate draft"))
                    try { Validate(JsonUtility.ToJson(_draft.Content)); _notice = "Definitions and all referenced sprites are valid. Source unchanged."; }
                    catch (Exception error) { _notice = error.Message; }
                if (GUILayout.Button("Save & Import")) Save();
            }
            if (GUILayout.Button("Reload from disk"))
            {
                if (!_dirty || EditorUtility.DisplayDialog("Discard draft changes?", "Your current draft will remain in a checkpoint backup. Reload replaces the in-memory draft with the source file.", "Reload", "Keep editing"))
                { Checkpoint(); LoadDisk(false); Checkpoint(); }
            }
            EditorGUILayout.LabelField(_dirty ? "Unsaved draft - source unchanged" : "Draft matches last loaded/saved source", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(_notice ?? "", MessageType.None);
        }
        private static string RowLabel(object row)
        {
            if (row == null) return "(empty record)";
            var type = row.GetType(); var id = (string)type.GetField("Id").GetValue(row);
            var name = (string)type.GetField("Name")?.GetValue(row);
            return string.IsNullOrEmpty(name) ? id ?? "(no ID)" : id + " - " + name;
        }
        private void DrawSprite(SerializedProperty record, bool hero)
        {
            var property = record.FindPropertyRelative("Art") ?? record.FindPropertyRelative("Background");
            if (property == null) return;
            var current = Resources.Load<Texture2D>("Art/" + property.stringValue + (hero ? "_idle" : ""));
            var picked = (Texture2D)EditorGUILayout.ObjectField("Sprite preview / picker", current, typeof(Texture2D), false);
            if (picked == null || picked == current) return;
            var path = AssetDatabase.GetAssetPath(picked); const string marker = "/Resources/Art/";
            var index = path.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) { _notice = "Place the texture under Assets/AshenSpire/Resources/Art first."; return; }
            var id = Path.ChangeExtension(path.Substring(index + marker.Length), null);
            if (hero)
            {
                if (!id.EndsWith("_idle", StringComparison.Ordinal)) { _notice = "Choose the hero's _idle texture. All five poses are checked on Save."; return; }
                id = id.Substring(0, id.Length - 5);
            }
            property.stringValue = id;
        }
        private void Change(string name, Action action)
        {
            _serialized.ApplyModifiedProperties();
            Undo.RegisterCompleteObjectUndo(_draft, name); action(); EditorUtility.SetDirty(_draft);
            _serialized.Update(); RefreshDirty();
        }
        private void RefreshDirty()
        {
            _dirty = _draft != null && JsonUtility.ToJson(_draft.Content) != _savedJson;
            titleContent = new GUIContent("Campaign Content" + (_dirty ? " *" : ""));
        }
        private void OnUndo() { if (_draft == null) return; _serialized?.Update(); RefreshDirty(); Repaint(); }
        private static void Validate(string json)
        {
            var content = JsonUtility.FromJson<CampaignDefinition>(json);
            CampaignAuthoringValidation.Validate(content, Path.Combine(UnityEngine.Application.dataPath, "AshenSpire/Resources/Art"));
            JsonUtility.FromJson<ContentDefinition>(File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/expedition.json"))).Validate();
        }
        private void Save()
        {
            try
            {
                _serialized.ApplyModifiedProperties();
                var result = CampaignContentFile.Save(Source, _sourceHash, JsonUtility.ToJson(_draft.Content, true) + "\n", Path.Combine(Repository, "Builds/ContentBackups"), Validate, BuildTools.ImportContent);
                _sourceHash = result.SourceHash; _savedJson = JsonUtility.ToJson(_draft.Content); RefreshDirty(); Checkpoint();
                _notice = result.Imported ? "Saved and imported. Restart Play mode or rebuild/reload a player. Backup: " + result.BackupPath : "Source saved and backed up, but import failed: " + result.ImportError + ". Fix the import and use Validate and Import Content; your source is already saved.";
            }
            catch (Exception error) { RefreshDirty(); _notice = "Source not saved: " + error.Message; }
        }
        private void Checkpoint()
        {
            if (_draft?.Content == null) return;
            try { CampaignContentFile.Checkpoint(Recovery, JsonUtility.ToJson(new CampaignDraftCheckpoint { DraftJson = JsonUtility.ToJson(_draft.Content), SavedJson = _savedJson, SourceHash = _sourceHash }, true)); _notice = "Draft checkpoint retained under Builds/AuthoringDrafts."; }
            catch (Exception error) { _notice = "Draft checkpoint failed: " + error.Message; Debug.LogWarning(_notice); }
        }
        private void OnDisable()
        {
            Checkpoint(); Undo.undoRedoPerformed -= OnUndo; AssemblyReloadEvents.beforeAssemblyReload -= Checkpoint;
            _serialized?.Dispose(); _serialized = null;
        }
        private void OnDestroy() { if (_draft != null) { Undo.ClearUndo(_draft); DestroyImmediate(_draft); } }
    }
}
