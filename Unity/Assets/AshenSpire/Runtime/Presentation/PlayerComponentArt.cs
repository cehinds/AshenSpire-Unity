using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    // Presentation only. Each mounted scene owns its resize callback and leaves
    // with the screen; no game state, global subscriptions or per-frame loading.
    internal static class PlayerComponentArt
    {
        private static Newtonsoft.Json.Linq.JObject _itemBounds;
        internal static VisualElement Item(string resource, string title)
        {
            var texture = Resources.Load<Texture2D>("Art/PlayerComponents/" + resource);
            if (texture == null) return null;
            var item = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = title };
            item.AddToClassList("guided-item");
            var illustration = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            // Rig parts retain their original canvas for character assembly.
            // Inventory previews frame only their visible pixels, without rewriting art.
            if (_itemBounds == null)
            {
                var boundsAsset = Resources.Load<TextAsset>("Art/PlayerComponents/item-bounds");
                _itemBounds = boundsAsset == null ? new Newtonsoft.Json.Linq.JObject() : Newtonsoft.Json.Linq.JObject.Parse(boundsAsset.text);
            }
            if (_itemBounds[resource] is Newtonsoft.Json.Linq.JArray bounds && bounds.Count == 4)
                illustration.sourceRect = new Rect((float)bounds[0], (float)bounds[1], (float)bounds[2], (float)bounds[3]);
            item.Add(illustration);
            item.Add(new Label(title) { pickingMode = PickingMode.Ignore });
            return item;
        }
        // JS build 1815 paintings (Art/js1815/ui/scene-*) replace the earlier owner scenes by key;
        // a key with no JS painting keeps its Art/PlayerComponents scene. The event room, which the
        // run panel paints as the spire vista, takes the JS grave dialogue stage.
        private static Texture2D Painting(string key, bool portrait)
        {
            var js = key == "spoils" || key == "aftermath" ? "spire-vista" : key;
            return Resources.Load<Texture2D>("Art/js1815/ui/scene-" + js + (portrait ? "-mobile" : "-desktop"))
                ?? Resources.Load<Texture2D>("Art/js1815/ui/scene-" + js)
                ?? Resources.Load<Texture2D>("Art/PlayerComponents/" + key + (portrait ? "-mobile" : "-desktop"));
        }
        internal static void Scene(VisualElement surface, string key)
        {
            if (key == "spire-vista" && surface.ClassListContains("run-phase-event")) key = "grave-dialogue";
            var host = surface.ClassListContains("app") ? surface : surface.GetFirstAncestorOfType<ScrollView>()?.parent;
            if (host == null) return;
            var previous = host.Q<Image>("player-component-scene");
            if (previous != null && previous.userData as string == key) return;
            previous?.RemoveFromHierarchy();
            host.Q("player-component-veil")?.RemoveFromHierarchy();
            var image = new Image { name = "player-component-scene", userData = key, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.AddToClassList("player-component-scene"); image.AddToClassList("scene-" + key);
            bool? portrait = null;
            void Fit()
            {
                var next = host.contentRect.width < host.contentRect.height;
                if (portrait == next) return;
                portrait = next;
                image.image = Painting(key, next);
            }
            image.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            host.Insert(0, image);
            var veil = new VisualElement { name = "player-component-veil", pickingMode = PickingMode.Ignore };
            veil.AddToClassList("player-component-veil"); veil.AddToClassList("scene-" + key); host.Insert(1, veil);
            Fit();
        }
    }
}
