// OriginalAppearance.cs — cosmetic tint frames and sigil badges shared by views.
// Author Original/appearance-options.json: original saved IDs and palette, plus
// 24-unit vector paths so Web/mobile never depend on an installed emoji font.
// Apply updates only the frame/badge; combat feedback owns the sprite's poses and
// temporary hit tint. Keep badges outside faces and independently of facing flips.
// This is presentation only: it never mutates a run, stats, equipment or saves.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public static class OriginalAppearance
    {
        private static JObject _options;
        public static JObject Options => (JObject)Data.DeepClone();
        private static JObject Data => _options ?? (_options = JObject.Parse(Resources.Load<TextAsset>("Original/appearance-options").text));
        private static JObject Row(string table,string id) => ((JArray)Data[table]).OfType<JObject>().FirstOrDefault(row => (string)row["id"] == id) ?? (JObject)Data[table][0];
        public static Color Tint(JObject cosmetic) { ColorUtility.TryParseHtmlString((string)Row("tints",(string)cosmetic?["tint"])["color"],out var color); return color; }
        public static VisualElement Badge(string name,JObject cosmetic)
        {
            var row = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
            row.style.minHeight = 30; row.style.marginTop = 3; row.style.marginBottom = 3; row.style.flexShrink = 0;
            var sigil = new OriginalSigil { name = "appearance-sigil" }; sigil.style.width = 26; sigil.style.height = 26; sigil.style.flexShrink = 0; row.Add(sigil);
            var label = new Label { name = "appearance-name" }; label.style.fontSize = 14; label.style.marginLeft = 7; label.style.flexShrink = 1; label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis; label.style.whiteSpace = WhiteSpace.NoWrap; row.Add(label);
            Apply(null,row,cosmetic); return row;
        }
        public static void Apply(Image portrait,VisualElement badge,JObject cosmetic)
        {
            var tint = Tint(cosmetic); var tintRow = Row("tints",(string)cosmetic?["tint"]); var sigil = Row("sigils",(string)cosmetic?["glyph"]);
            if (portrait != null)
            {
                portrait.style.borderBottomColor = tint; portrait.style.borderBottomWidth = 2;
                portrait.tooltip = (string)tintRow["name"] + " · " + (string)sigil["name"];
            }
            if (badge == null) return;
            badge.Q<OriginalSigil>("appearance-sigil").Configure(sigil,tint);
            var label = badge.Q<Label>("appearance-name"); label.text = ((string)cosmetic?["name"] ?? "Forsaken") + " · " + (string)sigil["name"]; label.style.color = tint;
            badge.tooltip = (string)tintRow["name"] + " · " + (string)sigil["name"];
        }
    }
    // An original saved glyph rendered as small, font-independent UI geometry.
    // Shape data is separate from the renderer. Keep the 24-unit view box fixed.
    public sealed class OriginalSigil : VisualElement
    {
        private JObject _definition;
        private Color _color;
        public OriginalSigil() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        public void Configure(JObject definition,Color color) { _definition = (JObject)definition.DeepClone(); _color = color; tooltip = (string)definition["name"]; MarkDirtyRepaint(); }
        private void Draw(MeshGenerationContext context)
        {
            if (_definition == null) return;
            var size = Math.Min(contentRect.width,contentRect.height); if (size <= 0) return;
            var origin = new Vector2(contentRect.x + (contentRect.width - size) / 2,contentRect.y + (contentRect.height - size) / 2); var scale = size / 28;
            Vector2 Point(float x,float y) => origin + new Vector2((x + 2) * scale,(y + 2) * scale);
            var painter = context.painter2D; painter.lineWidth = Math.Max(1.25f,size / 16);
            foreach (var shape in _definition["shapes"])
            {
                painter.strokeColor = _color; painter.fillColor = (bool?)shape["cutout"] == true ? new Color(.09f,.075f,.06f,1) : _color; painter.BeginPath();
                if (shape["circle"] is JArray circle)
                {
                    for (var index = 0; index < 24; index++) { var angle = index * Mathf.PI * 2 / 24; var point = Point((float)circle[0] + Mathf.Cos(angle) * (float)circle[2],(float)circle[1] + Mathf.Sin(angle) * (float)circle[2]); if (index == 0) painter.MoveTo(point); else painter.LineTo(point); }
                    painter.ClosePath();
                }
                else
                {
                    var points = (JArray)shape["points"]; for (var index = 0; index < points.Count; index++) { var point = Point((float)points[index][0],(float)points[index][1]); if (index == 0) painter.MoveTo(point); else painter.LineTo(point); }
                    if ((bool?)shape["closed"] == true || (bool?)shape["fill"] == true) painter.ClosePath();
                }
                if ((bool?)shape["fill"] == true) painter.Fill(); else painter.Stroke();
            }
        }
    }
}
