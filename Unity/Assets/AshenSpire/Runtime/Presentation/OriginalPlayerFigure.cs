// OriginalPlayerFigure.cs — four distinct original sprite styles in UI Toolkit.
// WIRING: configure from a creator/run's saved customization, class and active
// armour. Resources/Original/sprite-styles.json indexes original imported art.
// Animated uses original crop registration; Rendered/Classic are fixed art;
// Sigil draws the selected symbol in its own panel. Only Animated accepts poses.
// CombatFeedback owns timing/reduced motion; this component owns pose selection
// and settling, never a second timer. A weak run owner keeps attack rotation
// across rebuilt trees without retaining old views or changing save/game state.
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalPlayerFigure : Image
    {
        private static OriginalSpriteCatalog _catalog;
        public static OriginalSpriteCatalog Catalog => _catalog ?? (_catalog = new OriginalSpriteCatalog(JObject.Parse(Resources.Load<TextAsset>("Original/sprite-styles").text)));
        private static readonly ConditionalWeakTable<object,OriginalSpritePlayback> Playback = new ConditionalWeakTable<object,OriginalSpritePlayback>();
        private readonly VisualElement _viewport = new VisualElement();
        private readonly Image _art = new Image { scaleMode = ScaleMode.StretchToFill };
        private readonly OriginalSigil _sigil = new OriginalSigil();
        private readonly OriginalSpritePlayback _playback;
        private JObject _plan, _customization;
        private string _pose = "idle", _figureId;
        public string RenderStyle => (string)_plan?["style"];
        public string Pose => _pose;
        public OriginalPlayerFigure(object owner,string figureId = null)
        {
            _playback = Playback.GetOrCreateValue(owner ?? throw new ArgumentNullException(nameof(owner))); _figureId = figureId;
            pickingMode = PickingMode.Ignore; style.overflow = Overflow.Visible;
            _viewport.style.position = Position.Absolute; _viewport.style.overflow = Overflow.Visible; _viewport.pickingMode = PickingMode.Ignore; Add(_viewport);
            _art.style.position = Position.Absolute; _art.pickingMode = PickingMode.Ignore; _viewport.Add(_art);
            _sigil.style.position = Position.Absolute; _viewport.Add(_sigil);
            RegisterCallback<GeometryChangedEvent>(_ => Place());
        }
        public void Configure(string classId,JObject customization,string armourId = "default")
        {
            _customization = customization == null ? new JObject() : (JObject)customization.DeepClone();
            _plan = Catalog.Resolve(classId,_customization,armourId); _pose = "idle";
            var glyphs = (JArray)OriginalAppearance.Options["sigils"]; var glyph = (string)_customization["glyph"];
            var sigil = glyphs.OfType<JObject>().FirstOrDefault(row => (string)row["id"] == glyph) ?? glyphs.OfType<JObject>().First(row => (string)row["id"] == "🛡");
            _sigil.Configure(sigil,OriginalAppearance.Tint(_customization));
            var resource = (string)_plan["resource"]; var texture = string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);
            // Source classSprite falls back to the classic silhouette if art fails.
            if (texture == null && RenderStyle != "glyph")
            {
                _plan["style"] = "classic"; var row = Catalog.Class(classId); var tint = (string)_customization["tint"] ?? "gold";
                texture = Resources.Load<Texture2D>((string)(row?["classic"]?[tint] ?? row?["classic"]?["gold"]) ?? "");
                if (texture == null) _plan["style"] = "glyph";
            }
            _art.image = texture; _art.tintColor = Color.white; tooltip = (string)Catalog.Styles.First(row => (string)row["id"] == RenderStyle)["name"];
            Place();
        }
        public static string ActiveArmour(JObject loadout)
        {
            var values = loadout?["sets"]?["armor"] as JArray; var index = (int?)loadout?["active"]?["armor"] ?? 0;
            return values != null && index >= 0 && index < values.Count ? (string)values[index] ?? "default" : "default";
        }
        public bool SetPose(string pose)
        {
            if (RenderStyle != "animated") return false;
            var frame = Catalog.Frame((string)_plan["poseClass"],pose,(string)_plan["tint"]); if (frame == null) return false;
            var texture = Resources.Load<Texture2D>((string)frame["resource"]); if (texture == null) return false;
            _pose = pose; _art.image = texture; Place(); return true;
        }
        public void BeginFeedback(string cueId)
        {
            if (RenderStyle != "animated") return;
            var pose = cueId == "attack" ? "attack" : cueId == "hit" ? "hit" : "guard";
            SetPose(_playback.Resolve(Catalog,_plan,_figureId ?? (string)_customization["figureId"] ?? (string)_plan["poseClass"] + "_" + (string)_plan["tint"],pose));
        }
        public void FeedbackTint(Color color) { _art.tintColor = color; }
        public void Settle() { SetPose("idle"); _art.tintColor = Color.white; }
        private void Place()
        {
            if (_plan == null || contentRect.width <= 0 || contentRect.height <= 0) return;
            var scale = Math.Min(contentRect.width / 150,contentRect.height / 190); var width = 150 * scale; var height = 190 * scale;
            Rect(_viewport,(contentRect.width - width) / 2,(contentRect.height - height) / 2,width,height);
            var tint = OriginalAppearance.Tint(_customization); var glyph = RenderStyle == "glyph";
            _viewport.style.backgroundColor = glyph ? new Color(.165f,.141f,.094f,1) : Color.clear;
            Border(_viewport,glyph ? 2 * scale : 0,tint,glyph ? 10 * scale : 0);
            _art.style.display = glyph ? DisplayStyle.None : DisplayStyle.Flex;
            _sigil.style.display = DisplayStyle.None; Border(_sigil,0,tint,0); _sigil.style.backgroundColor = Color.clear;
            if (glyph) { ShowSigil(width / 2,height / 2,70 * scale,false,tint); return; }
            if (RenderStyle == "animated")
            {
                var rect = Catalog.FrameRect(_plan,_pose,width,height); if (rect != null) Rect(_art,(float)rect[0],(float)rect[1],(float)rect[2],(float)rect[3]); return;
            }
            var texture = _art.image as Texture2D; if (texture == null) return;
            var artScale = Math.Min(width / texture.width,height / texture.height); var artWidth = texture.width * artScale; var artHeight = texture.height * artScale;
            var left = (width - artWidth) / 2; var top = (height - artHeight) / 2; Rect(_art,left,top,artWidth,artHeight);
            var row = Catalog.Class((string)_plan["classId"]); if (string.IsNullOrEmpty((string)_customization["glyph"])) return;
            if (RenderStyle == "classic") { var anchor = (JArray)row["classicAnchor"]; ShowSigil(left + (float)anchor[0] / 110 * artWidth,top + (float)anchor[1] / 140 * artHeight,16f / 110 * artWidth,true,tint); }
            else if (row["renderedMedallionPct"]?.Type != JTokenType.Null) ShowSigil(width / 2,(float)row["renderedMedallionPct"] / 100 * height,22 * scale,true,tint);
        }
        private void ShowSigil(float x,float y,float size,bool medallion,Color tint)
        {
            _sigil.style.display = DisplayStyle.Flex; Rect(_sigil,x - size / 2,y - size / 2,size,size);
            if (medallion) { _sigil.style.backgroundColor = new Color(.078f,.063f,.047f,1); Border(_sigil,Math.Max(.6f,size / 15),tint,size / 2); }
        }
        private static void Rect(VisualElement element,float x,float y,float width,float height) { element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height; }
        private static void Border(VisualElement element,float width,Color color,float radius)
        {
            element.style.borderBottomWidth = width; element.style.borderTopWidth = width; element.style.borderLeftWidth = width; element.style.borderRightWidth = width;
            element.style.borderBottomColor = color; element.style.borderTopColor = color; element.style.borderLeftColor = color; element.style.borderRightColor = color;
            element.style.borderBottomLeftRadius = radius; element.style.borderBottomRightRadius = radius; element.style.borderTopLeftRadius = radius; element.style.borderTopRightRadius = radius;
        }
    }
}
