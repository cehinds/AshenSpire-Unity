// CampaignContentWindow.cs — owner record editor; changes stay in memory until validated.
// Open AshenSpire > Campaign Content Editor. Select a table and row, edit its JSON,
// and Apply Record. Save & Import validates references and keeps a timestamped backup.
// Art picker supplies Resources/Art names. Ordinary content requires no C# edits.
using System;
using System.IO;
using System.Linq;
using AshenSpire.Domain;
using UnityEditor;
using UnityEngine;
namespace AshenSpire.Editor
{
    public sealed class CampaignContentWindow : EditorWindow
    {
        private CampaignDefinition _content;
        private readonly string[] _tables = { "Cards", "Heroes", "Foes", "Encounters", "Equipment", "Tags" };
        private int _table, _row; private string _record, _notice; private Vector2 _scroll;
        private static string Repository => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
        [MenuItem("AshenSpire/Campaign Content Editor")]
        public static void Open() => GetWindow<CampaignContentWindow>("Campaign Content");
        private void OnEnable() => Reload();
        private void Reload()
        {
            try
            {
                _content = JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/campaign.json")));
                SelectRecord();
                _notice = "Loaded authoritative campaign JSON.";
            }
            catch (Exception error) { _notice = error.Message; }
        }
        private Array Rows => (Array)typeof(CampaignDefinition).GetField(_tables[_table]).GetValue(_content);
        private void SelectRecord()
        {
            _row = Mathf.Clamp(_row, 0, Math.Max(0, Rows.Length - 1));
            _record = Rows.Length > 0 ? JsonUtility.ToJson(Rows.GetValue(_row), true) : "{}";
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Campaign records", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edit JSON values; effects and tags are reusable components. Apply changes to the selected record, then Save & Import. CSV round-trip: tools/campaign-table.py.", MessageType.Info);
            if (_content == null)
            {
                if (GUILayout.Button("Reload"))
                    Reload();
                EditorGUILayout.LabelField(_notice ?? "");
                return;
            }
            var table = EditorGUILayout.Popup("Table", _table, _tables);
            if (table != _table)
            {
                _table = table;
                _row = 0;
                SelectRecord();
            }
            var rows = Rows;
            var names = rows.Cast<object>().Select(row => (string)row.GetType().GetField("Id").GetValue(row)).ToArray();
            var selected = EditorGUILayout.Popup("Record", _row, names);
            if (selected != _row)
            {
                _row = selected;
                SelectRecord();
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _record = EditorGUILayout.TextArea(_record, GUILayout.MinHeight(280));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Apply Record"))
            {
                try
                {
                    var type = rows.GetType().GetElementType();
                    var value = JsonUtility.FromJson(_record, type);
                    rows.SetValue(value, _row);
                    _notice = "Record applied in memory. Save validates the complete content set.";
                }
                catch (Exception error) { _notice = error.Message; }
            }
            if (GUILayout.Button("Duplicate selected record"))
            {
                var type = rows.GetType().GetElementType();
                var expanded = Array.CreateInstance(type, rows.Length + 1);
                Array.Copy(rows, expanded, rows.Length);
                var copy = JsonUtility.FromJson(_record, type);
                type.GetField("Id").SetValue(copy, "new." + Guid.NewGuid().ToString("N").Substring(0, 8));
                expanded.SetValue(copy, rows.Length);
                typeof(CampaignDefinition).GetField(_tables[_table]).SetValue(_content, expanded);
                _row = rows.Length;
                SelectRecord();
            }
            var texture = (Texture2D)EditorGUILayout.ObjectField("Inspect / pick sprite", null, typeof(Texture2D), false);
            if (texture != null)
            {
                var asset = AssetDatabase.GetAssetPath(texture);
                _notice = asset.Contains("/Resources/Art/") ? "Use Art: \"" + Path.GetFileNameWithoutExtension(asset) + "\" in the record (heroes use the prefix before _idle)." : "Place the sprite under Assets/AshenSpire/Resources/Art first.";
            }
            if (GUILayout.Button("Save & Import"))
                Save();
            if (GUILayout.Button("Reload from disk (discard in-memory edits)"))
                Reload();
            EditorGUILayout.HelpBox(_notice ?? "", MessageType.None);
        }
        private void Save()
        {
            try
            {
                Rows.SetValue(JsonUtility.FromJson(_record, Rows.GetType().GetElementType()), _row);
                _content.Validate();
                foreach (var name in _content.Foes.Select(x => x.Art).Concat(_content.Encounters.Select(x => x.Background)).Concat(_content.Heroes.SelectMany(x => new[] { x.Art + "_idle", x.Art + "_attack1", x.Art + "_attack2", x.Art + "_guard", x.Art + "_hit" })))
                    if (Resources.Load<Texture2D>("Art/" + name) == null)
                        throw new ArgumentException("Missing Resources/Art/" + name + ". Supply the sprite before saving.");
                var source = Path.Combine(Repository, "GameContent/Unity/campaign.json");
                var backup = Path.Combine(Repository, "Builds/ContentBackups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
                Directory.CreateDirectory(backup);
                File.Copy(source, Path.Combine(backup, "campaign.json"));
                File.WriteAllText(source, JsonUtility.ToJson(_content, true));
                BuildTools.ImportContent();
                _notice = "Saved, validated and imported. Stop/restart Play mode, or rebuild/reload the player, to load changed content.";
            }
            catch (Exception error) { _notice = "Not saved: " + error.Message; }
        }
    }
}
