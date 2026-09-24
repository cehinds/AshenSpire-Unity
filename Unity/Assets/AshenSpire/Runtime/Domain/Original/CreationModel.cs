// CreationModel.cs — original attribute allocation and derived-resource preview.
// Edit presets/modes/derivedStatRules in content. UI requests a delta; this model
// enforces bounds and allocation budget. Previewing never mutates a saved run.
// Rules is a copy of the resolved derived-stat rows (for benefit previews).
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class CreationModel
    {
        private readonly JObject _content;
        private readonly JObject _rules;
        private JObject _attributes;
        public string ClassId { get; private set; }
        public string ModeId { get; private set; }
        public event Action Changed;
        public CreationModel(OriginalContentCatalog catalog, string classId, string modeId, AttributeProgression progression = null)
        {
            _content = catalog.Data(); _rules = DerivedStatCalculator.Resolve((JObject)_content["derivedStatRules"], progression?.DerivedLayer());
            Select(classId, modeId);
        }
        public void Select(string classId, string modeId)
        {
            if (!_content["classes"].Any(x => (string)x["id"] == classId) || !_content["creationModes"].Any(x => (string)x["id"] == modeId)) throw new ArgumentException("Unknown class or creation mode.");
            var preset = _content["attributeRules"]["presets"]?[modeId]?[classId] as JObject ?? throw new ArgumentException("Missing creation preset.");
            ClassId = classId; ModeId = modeId; _attributes = (JObject)preset.DeepClone(); Changed?.Invoke();
        }
        // Modes offered at creation: characterCreation.visibleModeIds, in order, each resolved
        // against creationModes as web creationModeViews does. Unlike the web, the default mode
        // is added if the list omits it, and a missing list offers every mode. Hidden modes
        // stay in creationModes so existing saves and Select() still resolve them.
        public static JArray VisibleModes(OriginalContentCatalog catalog)
        {
            var data = catalog.Data(); var modes = (JArray)data["creationModes"];
            var ids = (data["characterCreation"]?["visibleModeIds"] as JArray)?.Select(x => (string)x).ToList() ?? modes.Select(x => (string)x["id"]).ToList();
            var fallback = (string)data["attributeRules"]["defaultMode"];
            if (!ids.Contains(fallback)) ids.Insert(0, fallback);
            return new JArray(ids.Distinct().Select(id => modes.FirstOrDefault(x => (string)x["id"] == id) ?? throw new ArgumentException("characterCreation.visibleModeIds: creation mode '" + id + "' does not resolve.")));
        }
        private JObject Mode => (JObject)_content["creationModes"].First(x => (string)x["id"] == ModeId);
        public JObject Attributes() => (JObject)_attributes.DeepClone();
        public JObject Rules => (JObject)_rules.DeepClone();
        public int TotalPoints => (int)Mode["baseline"] * _content["attributes"].Count() + (int)Mode["bonusPool"];
        public int Minimum => (string)Mode["belowBaseline"] == "forbid" ? Math.Max((int)Mode["minimum"], (int)Mode["baseline"]) : (int)Mode["minimum"];
        public int Maximum => (int)Mode["maximum"];
        public int Remaining => TotalPoints - _attributes.Properties().Sum(x => (int)x.Value);
        public bool CanBegin => Remaining == 0;
        public bool CanAdjust(string attribute, int delta)
        {
            if (_attributes[attribute] == null || (delta != -1 && delta != 1)) return false;
            var value = (int)_attributes[attribute] + delta;
            return value >= Minimum && value <= Maximum && (delta < 0 || Remaining >= delta);
        }
        public bool Adjust(string attribute, int delta)
        {
            if (!CanAdjust(attribute, delta)) return false;
            _attributes[attribute] = (int)_attributes[attribute] + delta; Changed?.Invoke(); return true;
        }
        public JObject Resources()
        {
            var result = new JObject(); var hero = (JObject)_content["classes"].First(x => (string)x["id"] == ClassId);
            foreach (var row in _rules.Properties()) result[row.Name] = DerivedStatCalculator.Receipt(_rules, row.Name, _attributes, hero)["value"];
            return result;
        }
    }
}
