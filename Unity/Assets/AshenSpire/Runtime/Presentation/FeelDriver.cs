// FeelDriver.cs — Presentation's single entry point to the F07 feel profile.
// Loads Resources/Feel/feel-profile.json once (JsonUtility → FeelProfile, validated), holds
// the player's motion settings (CampaignView's Reduced motion / Quick animations toggles →
// FeelSettings.FromToggles), applies sampled transforms, and owns the hand-card hover lift.
// EDIT timings in the HTML first, then feel-profile.json (docs/Unity-Feel.md); never here.
// GEOMETRY CONTRACT: feel transforms are visual only. Every element they move is tracked,
// and SettledBound reports a control's bounds as if no feel transform applied, so pointer
// playtests (tools/native-ui-driver.cjs) keep clicking the same settled geometry as before.
using System;
using System.Collections.Generic;
using AshenSpire.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public static class FeelDriver
    {
        private static FeelProfile _profile;
        private static readonly HashSet<VisualElement> Moved = new HashSet<VisualElement>();
        public static FeelProfile Profile => _profile ?? Load();
        public static FeelSettings Settings { get; private set; } = new FeelSettings();

        /// <summary>Loads and validates the profile. Called once at startup by CampaignView.</summary>
        public static FeelProfile Load()
        {
            if (_profile != null) return _profile;
            var asset = Resources.Load<TextAsset>(FeelProfile.ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing Resources/" + FeelProfile.ResourcePath + ".json.");
            var profile = JsonUtility.FromJson<FeelProfile>(asset.text);
            var errors = profile?.Validate() ?? new List<string> { "feel-profile.json: unreadable." };
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            return _profile = profile;
        }

        public static void Configure(bool reducedMotion, bool quickAnimations) => Settings = FeelSettings.FromToggles(reducedMotion, quickAnimations);

        /// <summary>Writes a sampled transform. Rest values (0, 0, 1, 0°) clear the inline style.</summary>
        public static void Place(VisualElement element, float x, float y, float scale = 1, float rotate = 0)
        {
            if (element == null) return;
            var moved = x != 0 || y != 0 || scale != 1 || rotate != 0;
            if (!moved) { Rest(element); return; }
            element.style.translate = new Translate(x, y, 0);
            element.style.scale = new Scale(new Vector3(scale, scale, 1));
            element.style.rotate = new Rotate(new Angle(rotate, AngleUnit.Degree));
            Moved.Add(element);
        }

        public static void Rest(VisualElement element)
        {
            if (element == null) return;
            element.style.translate = StyleKeyword.Null;
            element.style.scale = StyleKeyword.Null;
            element.style.rotate = StyleKeyword.Null;
            Moved.Remove(element);
        }

        /// <summary>A control's world bounds with any feel transform (its own or an ancestor's) removed.</summary>
        public static Rect SettledBound(VisualElement control)
        {
            if (Moved.Count == 0 || control == null) return control?.worldBound ?? Rect.zero;
            for (var moved = control; moved != null; moved = moved.parent)
            {
                if (!Moved.Contains(moved)) continue;
                if (moved.parent == null) break;
                var own = new Rect(0, 0, control.layout.width, control.layout.height);
                var local = moved == control ? own : control.ChangeCoordinatesTo(moved, own);
                return moved.parent.LocalToWorld(new Rect(local.position + moved.layout.position, local.size));
            }
            return control.worldBound;
        }

        /// <summary>
        /// Hand cards lift and grow on hover (card.hover: kit.css .card transition 140 ms `ease`,
        /// combat.css .hand .card:hover translateY(-56px) scale(1.32), origin bottom centre, z-index 50).
        /// The real card never moves: an unnamed, unpickable copy is drawn in an overlay layer at the
        /// top of the view (so the rail cannot clip it and no neighbour draws over it), and the real
        /// card is hidden (opacity only) while the copy shows. Control order, names and settled
        /// bounds are unchanged; pointer input still lands on the real card. Only cards inside a
        /// hand rail react. Reduced motion resolves card.hover to nothing, so no copy appears.
        /// </summary>
        public static void HandCardHover(OriginalCardView card)
        {
            var state = new HoverState();
            card.RegisterCallback<PointerEnterEvent>(_ => Hover(card, state, true));
            card.RegisterCallback<PointerLeaveEvent>(_ => Hover(card, state, false));
            card.RegisterCallback<DetachFromPanelEvent>(_ => EndHover(card, state));
        }

        /// <summary>True for elements drawn in the feel overlay (copies, never controls). Reports skip them.</summary>
        public static bool InOverlay(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent) if (e.ClassListContains(OverlayClass)) return true;
            return false;
        }

        private const string OverlayClass = "feel-overlay";
        private sealed class HoverState { public double Amount; public FeelTween Tween; public OriginalCardView Ghost; public IVisualElementScheduledItem Follow; }

        private static void Hover(OriginalCardView card, HoverState state, bool entering)
        {
            if (entering && !(card.parent?.ClassListContains("original-hand-cards") ?? false)) return;
            var hover = Profile.Resolve("card.hover", Settings);
            state.Tween?.Stop(); state.Tween = null;
            if (!hover.Play) { EndHover(card, state); return; }
            var from = state.Amount; var to = entering ? 1.0 : 0.0;
            // A CSS transition reversed mid-way runs for the part it has to undo (reversing shortening).
            var duration = hover.DurationMs * Math.Abs(to - from);
            if (duration <= 0) return;
            if (state.Ghost == null && !ShowGhost(card, state)) return;
            var lift = (float)hover.Sample(FeelProperty.Y, 1); var grow = (float)hover.Sample(FeelProperty.Scale, 1);
            state.Tween = FeelTween.Run(card, duration, ms =>
            {
                state.Amount = from + (to - from) * hover.Curve.Evaluate(ms / duration);
                var h = (float)state.Amount;
                if (state.Ghost != null) Place(state.Ghost, 0, lift * h, 1 + (grow - 1) * h);
            }, status => { if (status == "completed" && to == 0) EndHover(card, state); });
        }

        private static bool ShowGhost(OriginalCardView card, HoverState state)
        {
            var layer = Overlay(card);
            if (layer == null || card.Ghost == null) return false;
            var ghost = card.Ghost();
            ghost.pickingMode = PickingMode.Ignore; ghost.focusable = false;
            ghost.Query<VisualElement>().ForEach(child => child.pickingMode = PickingMode.Ignore);
            ghost.style.position = Position.Absolute;
            ghost.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0);
            ghost.AddToClassList("feel-hover-ghost");
            layer.Add(ghost);
            state.Ghost = ghost;
            void Follow()
            {
                if (card.panel == null) { EndHover(card, state); return; }
                var bound = card.worldBound; var at = layer.WorldToLocal(bound.position);
                ghost.style.left = at.x; ghost.style.top = at.y;
                ghost.style.width = bound.width; ghost.style.minWidth = bound.width; ghost.style.maxWidth = bound.width;
                ghost.style.height = bound.height; ghost.style.minHeight = bound.height; ghost.style.maxHeight = bound.height;
            }
            Follow(); // tracks rail scrolling and relayout while the copy shows
            state.Follow = ghost.schedule.Execute(Follow).Every(16);
            card.style.opacity = 0; // visual only: the real card keeps its geometry and input
            return true;
        }

        private static void EndHover(VisualElement card, HoverState state)
        {
            state.Tween?.Stop(); state.Tween = null; state.Amount = 0;
            state.Follow?.Pause(); state.Follow = null;
            if (state.Ghost != null) { Rest(state.Ghost); state.Ghost.RemoveFromHierarchy(); state.Ghost = null; }
            card.style.opacity = StyleKeyword.Null;
        }

        // One overlay per view: the last child of the view root (the `.app` element), so copies
        // inherit its style sheets and fonts and draw above every screen element.
        private static VisualElement Overlay(VisualElement card)
        {
            VisualElement app = null;
            for (var e = card.parent; e != null; e = e.parent) if (e.ClassListContains("app")) app = e;
            if (app == null) return null;
            var layer = app.Q(className: OverlayClass);
            if (layer == null)
            {
                layer = new VisualElement { name = "", pickingMode = PickingMode.Ignore };
                layer.AddToClassList(OverlayClass);
                layer.style.position = Position.Absolute; layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
                layer.style.overflow = Overflow.Visible;
            }
            if (layer.parent != app || app.IndexOf(layer) != app.childCount - 1) app.Add(layer); // keep on top
            return layer;
        }
    }
}
