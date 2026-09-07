// OriginalSpriteCatalog.cs — pure rendering policy imported from assets.js and
// PoseAnimator.js. Modify sprite-styles.json via its source importer. No Unity,
// gameplay state, timers or RNG. Frame registration keeps the idle pelvis/floor
// fixed while each original cropped pose changes its own dimensions.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Presentation
{
    public sealed class OriginalSpriteCatalog
    {
        private readonly JObject _data;
        public OriginalSpriteCatalog(JObject data) { _data = (JObject)data.DeepClone(); }
        public JArray Styles => (JArray)_data["styles"].DeepClone();
        public JObject Frame(string poseClass,string pose,string tint) => _data["frames"]?[poseClass + "_" + pose + "_" + tint] as JObject;
        public JObject Class(string classId) => string.IsNullOrEmpty(classId) ? null : _data["classes"]?[classId] as JObject;
        public JObject Resolve(string classId,JObject customization,string armourId = "default")
        {
            var selected = (string)customization?["spriteStyle"]; if (string.IsNullOrEmpty(selected)) selected = (string)_data["defaultStyle"];
            var tint = (string)customization?["tint"]; var known = Class(classId); var poseClass = classId;
            var style = "glyph"; string resource = null;
            if (known != null && selected != "glyph")
            {
                var outfit = string.IsNullOrEmpty(armourId) || armourId == "default" ? classId : classId + "-" + armourId;
                if (Frame(outfit,"idle",tint) != null) poseClass = outfit;
                if (selected == "animated" && Frame(poseClass,"idle",tint) != null) { style = "animated"; resource = (string)Frame(poseClass,"idle",tint)["resource"]; }
                else { style = selected == "classic" ? "classic" : "rendered"; resource = (string)(known[style]?[tint ?? ""] ?? known[style]?["gold"]); }
            }
            return new JObject { ["style"] = style, ["selectedStyle"] = selected, ["classId"] = classId, ["poseClass"] = poseClass, ["tint"] = tint, ["resource"] = resource };
        }
        public string[] Poses(JObject plan) => ((JArray)_data["strip"]).Values<string>().Where(p => Frame((string)plan["poseClass"],p,(string)plan["tint"]) != null).ToArray();
        public double[] FrameRect(JObject plan,string pose,double width,double height)
        {
            var idle = Frame((string)plan["poseClass"],"idle",(string)plan["tint"]); var frame = Frame((string)plan["poseClass"],pose,(string)plan["tint"]);
            if (idle == null || frame == null) return null;
            var scale = height / Math.Max(1,(double)idle["g"] - (double)idle["y"]);
            return new[] { width / 2 + ((double)frame["x"] - (double)idle["rx"]) * scale, height + ((double)frame["y"] - (double)idle["g"]) * scale, (double)frame["w"] * scale, (double)frame["h"] * scale };
        }
    }
    // Owned by one live run/creator via a weak owner key in OriginalPlayerFigure.
    // Rotation survives UI reconstruction without putting visual counters in saves.
    public sealed class OriginalSpritePlayback
    {
        private readonly Dictionary<string,int> _swings = new Dictionary<string,int>();
        public string Resolve(OriginalSpriteCatalog catalog,JObject plan,string figureId,string pose)
        {
            if (pose != "attack") return pose;
            var attacks = new[] { "attack1", "attack2", "attack3", "attack4" }.Where(p => catalog.Poses(plan).Contains(p)).ToArray();
            if (attacks.Length == 0) return pose;
            _swings.TryGetValue(figureId,out var count); _swings[figureId] = count + 1; return attacks[count % attacks.Length];
        }
    }
}
