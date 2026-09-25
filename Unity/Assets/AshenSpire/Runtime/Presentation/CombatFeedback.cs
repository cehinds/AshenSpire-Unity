// CombatFeedback.cs — one interruptible UI Toolkit timeline owned by CampaignView.
// TIMING comes from the F07 feel profile (FeelDriver.Profile, planned by FeelBeat):
// [ENEMY TURN banner.turn] → actor.lunge / actor.step → impact at WindupMs → hit.recoil /
// hit.recoilHeavy + screen.shake (heavy hits) + damageNumber.pop. Quick animations = HTML
// `fast` pacing; Reduced motion keeps opacity/colour tracks and drops all movement.
// EDIT: campaign.json/Feedback for poses, colours and sound; feel-profile.json for timing.
// No MonoBehaviour or scene wiring: Play receives the current view's sprite references.
// Cancel on every Shell/Dispose so detached screens cannot retain scheduled work.
using System;
using System.Collections.Generic;
using AshenSpire.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class CombatFeedback : IDisposable
    {
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private FeelTween _timeline;
        private Image _player, _enemy;
        private VisualElement _host;
        private Texture2D _idle;
        private Label _label;
        private Action<string> _report;
        private float _playerX, _enemyX;
        private bool _reportedImpact;
        public void Play(VisualElement host, Image player, Image enemy, string art, FeedbackCue cue, FeedbackOutcome outcome, bool enemyTurn, bool reduced, bool fast, bool diagnostics)
        {
            Cancel();
            _player = player; _enemy = enemy; _host = host;
            var settings = FeelSettings.FromToggles(reduced, fast);
            var beat = FeelBeat.Plan(FeelDriver.Profile, settings, enemyTurn, cue.Id == "attack", outcome.Damage, outcome.Hurt);
            _idle = _player is OriginalPlayerFigure ? null : Texture(art + "_idle");
            var originalFigure = _player as OriginalPlayerFigure;
            if (!reduced && !enemyTurn) originalFigure?.BeginFeedback(cue.Id);
            var text = outcome.Describe();
            var description = string.IsNullOrEmpty(text) ? cue.Id.ToUpperInvariant() : text;
            _label = new Label(beat.BannerAtMs >= 0 ? "ENEMY TURN" : description) { pickingMode = PickingMode.Ignore, name = "combat-feedback" };
            _label.AddToClassList("combat-feedback");
            ColorUtility.TryParseHtmlString(cue.Color, out var color);
            _label.style.color = color;
            _label.style.opacity = 0;
            _label.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0); // num-pop: center bottom
            host.Add(_label);
            var frames = new Texture2D[cue.Poses.Length];
            if (!(_player is OriginalPlayerFigure)) for (var i = 0; i < frames.Length; i++) frames[i] = Texture(art + "_" + cue.Poses[i]);
            var playerGlow = beat.Victim == FeelVictim.None && (outcome.Healing > 0 || outcome.Block > 0);
            var enemyGlow = beat.Victim == FeelVictim.None && !enemyTurn && outcome.Poison > 0;
            _report = diagnostics ? status => Debug.Log("ASHENSPIRE_FEEDBACK " + JsonUtility.ToJson(new FeedbackReport { Cue = cue.Id, Status = status, Reduced = reduced, Fast = fast, Speed = beat.Pace.SpeedId, Duration = beat.TotalMs / 1000.0, ImpactMs = beat.ImpactMs, Text = _label?.text, PlayerX = _playerX, EnemyX = _enemyX, SpriteStyle = (_player as OriginalPlayerFigure)?.RenderStyle, Pose = (_player as OriginalPlayerFigure)?.Pose })) : null;
            _report?.Invoke("started");
            var impacted = false; var baseFont = 0f;
            _timeline = FeelTween.Run(host, beat.TotalMs, ms =>
            {
                if (!impacted && ms >= beat.ImpactMs)
                {
                    impacted = true;
                    if (!reduced && enemyTurn) originalFigure?.BeginFeedback(cue.Id);
                    _label.text = description; _label.style.letterSpacing = StyleKeyword.Null;
                    baseFont = _label.resolvedStyle.fontSize;
                    if (baseFont > 0 && beat.NumberFontScale != 1) _label.style.fontSize = baseFont * beat.NumberFontScale;
                }
                // Actor: lunge toward the foe, or a step for non-attack actions.
                var actor = beat.ActorIsPlayer ? _player : _enemy;
                var actorMs = ms - beat.ActorAtMs;
                var actorX = actorMs >= 0 ? (float)(beat.ActorSide * beat.Actor.SampleAt(FeelProperty.X, actorMs)) : 0;
                var actorY = actorMs >= 0 ? (float)beat.Actor.SampleAt(FeelProperty.Y, actorMs) : 0;
                var actorScale = actorMs >= 0 ? (float)beat.Actor.SampleAt(FeelProperty.Scale, actorMs) : 1;
                var actorBright = actorMs >= 0 ? (float)beat.Actor.SampleAt(FeelProperty.Brightness, actorMs) : 1;
                // Victim: recoil away from the blow with a white-hot flash (tinted by the cue colour).
                var hitMs = ms - beat.ImpactMs;
                var victim = beat.Victim == FeelVictim.Player ? _player : beat.Victim == FeelVictim.Enemy ? _enemy : null;
                var victimX = hitMs >= 0 ? (float)(beat.VictimSide * beat.Recoil.SampleAt(FeelProperty.X, hitMs)) : 0;
                var victimRotate = hitMs >= 0 ? (float)(beat.VictimSide * beat.Recoil.SampleAt(FeelProperty.Rotate, hitMs)) : 0;
                var victimBright = hitMs >= 0 ? (float)beat.Recoil.SampleAt(FeelProperty.Brightness, hitMs) : 1;
                var glowBright = hitMs >= 0 ? (float)beat.Glow.SampleAt(FeelProperty.Brightness, hitMs) : 1;
                float Bright(Image image) => Math.Max(image == actor ? actorBright : 1, Math.Max(image == victim ? victimBright : 1, (image == _player && playerGlow) || (image == _enemy && enemyGlow) ? glowBright : 1));
                if (_player != null)
                {
                    if (!(_player is OriginalPlayerFigure) && !reduced) _player.image = frames[Math.Min(frames.Length - 1, (int)(Mathf.Clamp01((float)(Math.Max(0, actorMs) / Math.Max(1, beat.TotalMs - beat.ActorAtMs))) * frames.Length))];
                    _playerX = (_player == actor ? actorX : 0) + (_player == victim ? victimX : 0);
                    FeelDriver.Place(_player, _playerX, _player == actor ? actorY : 0, _player == actor ? actorScale : 1, _player == victim ? victimRotate : 0);
                    Tint(_player, Color.Lerp(Color.white, color, Flash(Bright(_player)) * .55f));
                }
                if (_enemy != null)
                {
                    _enemyX = (_enemy == actor ? actorX : 0) + (_enemy == victim ? victimX : 0);
                    FeelDriver.Place(_enemy, _enemyX, _enemy == actor ? actorY : 0, _enemy == actor ? actorScale : 1, _enemy == victim ? victimRotate : 0);
                    Tint(_enemy, Color.Lerp(Color.white, color, Flash(Bright(_enemy)) * .7f));
                }
                // Screen shake on the combat stage: heavy hits only, ±4 px, decays to rest in 200 ms.
                if (beat.Shake.Play) FeelDriver.Place(host, hitMs >= 0 ? (float)beat.Shake.SampleAt(FeelProperty.X, hitMs) : 0, hitMs >= 0 ? (float)beat.Shake.SampleAt(FeelProperty.Y, hitMs) : 0);
                // Label: ENEMY TURN banner first (letter spacing closes in, fades), then the number pop.
                if (!impacted)
                {
                    var bannerMs = ms - beat.BannerAtMs;
                    var banner = beat.BannerAtMs >= 0 && beat.Banner.Play && bannerMs < beat.Banner.LifetimeMs;
                    _label.style.opacity = banner ? (float)beat.Banner.SampleAt(FeelProperty.Opacity, bannerMs) : 0;
                    if (banner && beat.Banner.Has(FeelProperty.LetterSpacing) && _label.resolvedStyle.fontSize > 0)
                        _label.style.letterSpacing = (float)beat.Banner.SampleAt(FeelProperty.LetterSpacing, bannerMs) * _label.resolvedStyle.fontSize;
                }
                else
                {
                    var alive = beat.Number.Play && hitMs < beat.Number.LifetimeMs;
                    _label.style.opacity = alive ? (float)beat.Number.SampleAt(FeelProperty.Opacity, hitMs) : 0;
                    FeelDriver.Place(_label, 0, (float)beat.Number.SampleAt(FeelProperty.Y, hitMs), (float)beat.Number.SampleAt(FeelProperty.Scale, hitMs));
                    if (hitMs >= 0 && _report != null && !_reportedImpact) { _reportedImpact = true; _report("impact"); }
                }
            }, Finish);
        }
        // brightness 1 → no flash; the recoil's 2.1–2.3 peak → full cue-colour flash.
        private static float Flash(float brightness) => Mathf.Clamp01(brightness - 1);
        private static void Tint(Image image, Color tint)
        {
            if (image is OriginalPlayerFigure figure) figure.FeedbackTint(tint);
            else if (image is OriginalEnemyFigure enemyFigure) enemyFigure.FeedbackTint(tint);
            else image.tintColor = tint;
        }
        private Texture2D Texture(string id)
        {
            if (!_textures.TryGetValue(id, out var texture)) { texture = Resources.Load<Texture2D>("Art/" + id); _textures.Add(id, texture); }
            return texture;
        }
        public void Cancel() { _timeline?.Stop("cancelled"); }
        private void Finish(string status)
        {
            _timeline = null; _reportedImpact = false;
            if (_player != null) { if (_player is OriginalPlayerFigure figure) figure.Settle(); else { _player.image = _idle; _player.tintColor = Color.white; } FeelDriver.Rest(_player); }
            if (_enemy != null) { FeelDriver.Rest(_enemy); Tint(_enemy, Color.white); }
            FeelDriver.Rest(_host);
            _playerX = 0; _enemyX = 0;
            _report?.Invoke(status == "detached" ? "cancelled" : status); _report = null;
            if (_label != null) FeelDriver.Rest(_label);
            _label?.RemoveFromHierarchy(); _label = null; _player = null; _enemy = null; _host = null;
        }
        public void Dispose() { Cancel(); _textures.Clear(); }
        [Serializable] private sealed class FeedbackReport { public string Cue, Status, Text, SpriteStyle, Pose, Speed; public bool Reduced, Fast; public double Duration; public int ImpactMs; public float PlayerX, EnemyX; }
    }
}
