// Native card anatomy from the owner's 4175 visual reference. Resolved rules
// and prices are supplied by the existing game; this view has no play authority.
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OwnerCardFace : VisualElement
    {
        public OwnerCardFace(JObject card, JObject cost, string title, string rules, string typeName)
        {
            AddToClassList("owner-card-face"); pickingMode = PickingMode.Ignore;
            var type = (string)card["type"];
            var art = type == "attack" ? "attack" : type == "skill" && (card["effects"] as JArray)?.Count == 1 && (string)card["effects"][0]["op"] == "block" ? "guard" : "ember";
            var name = Copy(title,"owner-card-name"); Add(name);
            var image = new Image { image = Resources.Load<Texture2D>("Art/OwnerAppearance/card-" + art), scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.AddToClassList("owner-card-art"); Add(image);
            var band = Copy(typeName.ToUpperInvariant() + ((bool?)card["upgraded"] == true ? " · UPGRADED" : ""),"owner-card-kind"); band.AddToClassList(art); Add(band);
            var description = Copy(rules,"owner-card-rules");
            description.style.fontSize = rules.Length > 110 ? 14 : rules.Length > 65 ? 16 : 19;
            Add(description);
            var action = (bool?)cost["variable"] == true ? "X" : (string)cost["action"];
            Add(Copy(action,"owner-card-price"));
            if ((int?)cost["mana"] > 0) Add(Copy("MP " + cost["mana"],"owner-card-extra-price"));
            // The frozen older rules still have a separately charged pool.
            if ((int?)cost["stamina"] > 0)
                Add(Copy("SP " + cost["stamina"],"owner-card-stamina-price"));
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                var width = e.newRect.width;
                if (width <= 0) return;
                // The face always stays 2:3; the containing rail may wrap or pan.
                style.height = width * 1.5f; style.minHeight = width * 1.5f;
                name.style.height = width * .23f;
                name.style.fontSize = Mathf.Clamp(width * .12f, 17, 24);
                image.style.height = width * .49f;
                band.style.height = width * .13f;
                description.style.fontSize = Mathf.Clamp(width * (rules.Length > 110 ? .075f : rules.Length > 65 ? .088f : .11f), 11, 21);
            });
        }
        private static Label Copy(string text,string style)
        { var label = new Label(text) { pickingMode = PickingMode.Ignore, tooltip = text }; label.AddToClassList(style); return label; }
    }
}
