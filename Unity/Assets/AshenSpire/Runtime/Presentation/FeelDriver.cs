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
        /// combat.css .hand .card:hover translateY(-56px) scale(1.32), origin bottom centre).
        /// Only cards inside a hand rail react; previews elsewhere stay still. Reduced motion
        /// resolves card.hover to nothing (movement-only), so the card does not move.
        /// </summary>
        public static void HandCardHover(VisualElement card)
        {
            var state = new HoverState();
            card.RegisterCallback<PointerEnterEvent>(_ => Hover(card, state, true));
            card.RegisterCallback<PointerLeaveEvent>(_ => Hover(card, state, false));
            card.RegisterCallback<DetachFromPanelEvent>(_ => { state.Tween?.Stop(); state.Tween = null; state.Amount = 0; Rest(card); });
        }

        private sealed class HoverState { public double Amount; public FeelTween Tween; }

        private static void Hover(VisualElement card, HoverState state, bool entering)
        {
            if (entering && !(card.parent?.ClassListContains("original-hand-cards") ?? false)) return;
            var hover = Profile.Resolve("card.hover", Settings);
            state.Tween?.Stop(); state.Tween = null;
            if (!hover.Play) { state.Amount = 0; Rest(card); return; }
            var from = state.Amount; var to = entering ? 1.0 : 0.0;
            // A CSS transition reversed mid-way runs for the part it has to undo (reversing shortening).
            var duration = hover.DurationMs * Math.Abs(to - from);
            if (duration <= 0) return;
            var lift = (float)hover.Sample(FeelProperty.Y, 1); var grow = (float)hover.Sample(FeelProperty.Scale, 1);
            card.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0);
            state.Tween = FeelTween.Run(card, duration, ms =>
            {
                state.Amount = from + (to - from) * hover.Curve.Evaluate(ms / duration);
                var h = (float)state.Amount;
                Place(card, 0, lift * h, 1 + (grow - 1) * h);
            }, status => { if (status == "completed" && to == 0) { state.Amount = 0; Rest(card); } });
        }
    }
}
