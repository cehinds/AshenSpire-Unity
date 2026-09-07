// CombatFeedback.cs — one interruptible UI Toolkit timeline owned by CampaignView.
// EDIT: campaign.json/Feedback for poses, timing, displacement, color and sound.
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
        private IVisualElementScheduledItem _timeline;
        private Image _player, _enemy;
        private Texture2D _idle;
        private Label _label;
        private Action<string> _report;
        public void Play(VisualElement host, Image player, Image enemy, string art, FeedbackCue cue, FeedbackOutcome outcome, bool enemyTurn, bool reduced, bool fast, bool diagnostics)
        {
            Cancel();
            _player = player; _enemy = enemy;
            _idle = _player is OriginalPlayerFigure ? null : Texture(art + "_idle");
            if (!reduced && _player is OriginalPlayerFigure originalFigure) originalFigure.BeginFeedback(cue.Id);
            var text = outcome.Describe();
            _label = new Label(string.IsNullOrEmpty(text) ? cue.Id.ToUpperInvariant() : text) { pickingMode = PickingMode.Ignore, name = "combat-feedback" };
            _label.AddToClassList("combat-feedback");
            ColorUtility.TryParseHtmlString(cue.Color, out var color);
            _label.style.color = color;
            host.Add(_label);
            var frames = new Texture2D[cue.Poses.Length];
            if (!(_player is OriginalPlayerFigure)) for (var i = 0; i < frames.Length; i++) frames[i] = Texture(art + "_" + cue.Poses[i]);
            var started = Time.realtimeSinceStartupAsDouble;
            var duration = cue.Milliseconds / 1000.0 * (fast ? .5 : 1);
            _report = diagnostics ? status => Debug.Log("ASHENSPIRE_FEEDBACK " + JsonUtility.ToJson(new FeedbackReport { Cue = cue.Id, Status = status, Reduced = reduced, Fast = fast, Duration = duration, Text = _label?.text, PlayerX = _player?.resolvedStyle.translate.x ?? 0, SpriteStyle = (_player as OriginalPlayerFigure)?.RenderStyle, Pose = (_player as OriginalPlayerFigure)?.Pose })) : null;
            _report?.Invoke("started");
            var reportedImpact = false;
            _timeline = host.schedule.Execute(() =>
            {
                var progress = Mathf.Clamp01((float)((Time.realtimeSinceStartupAsDouble - started) / duration));
                if (!reduced)
                {
                    var arc = Mathf.Sin(progress * Mathf.PI);
                    if (_player != null)
                    {
                        if (!(_player is OriginalPlayerFigure)) _player.image = frames[Math.Min(frames.Length - 1, (int)(progress * frames.Length))];
                        _player.style.translate = new Translate(arc * cue.Distance * (enemyTurn ? -.35f : 1), -arc * (enemyTurn ? 0 : 3));
                        var playerTint = Color.Lerp(Color.white, color, arc * (outcome.Hurt > 0 || outcome.Healing > 0 || outcome.Block > 0 ? .55f : 0));
                        if (_player is OriginalPlayerFigure figure) figure.FeedbackTint(playerTint); else _player.tintColor = playerTint;
                    }
                    if (_enemy != null)
                    {
                        _enemy.style.translate = new Translate(arc * cue.Distance * (enemyTurn ? -1 : .35f), 0);
                        var enemyTint = Color.Lerp(Color.white, color, arc * (outcome.Damage > 0 || outcome.Poison > 0 ? .7f : 0));
                        if (_enemy is OriginalEnemyFigure enemyFigure) enemyFigure.FeedbackTint(enemyTint); else _enemy.tintColor = enemyTint;
                    }
                    _label.style.translate = new Translate(0, -progress * 12);
                    _label.style.opacity = progress < .7f ? 1 : (1 - progress) / .3f;
                }
                if (!reportedImpact && progress >= .3f) { reportedImpact = true; _report?.Invoke("impact"); }
                if (progress >= 1) Finish("completed");
            }).Every(16);
        }
        private Texture2D Texture(string id)
        {
            if (!_textures.TryGetValue(id, out var texture)) { texture = Resources.Load<Texture2D>("Art/" + id); _textures.Add(id, texture); }
            return texture;
        }
        public void Cancel() { if (_timeline != null) Finish("cancelled"); }
        private void Finish(string status)
        {
            _timeline?.Pause(); _timeline = null;
            if (_player != null) { if (_player is OriginalPlayerFigure figure) figure.Settle(); else _player.image = _idle; _player.style.translate = new Translate(0, 0); _player.tintColor = Color.white; }
            if (_enemy != null) { _enemy.style.translate = new Translate(0, 0); if (_enemy is OriginalEnemyFigure enemyFigure) enemyFigure.FeedbackTint(Color.white); else _enemy.tintColor = Color.white; }
            _report?.Invoke(status); _report = null;
            _label?.RemoveFromHierarchy(); _label = null; _player = null; _enemy = null;
        }
        public void Dispose() { Cancel(); _textures.Clear(); }
        [Serializable] private sealed class FeedbackReport { public string Cue, Status, Text, SpriteStyle, Pose; public bool Reduced, Fast; public double Duration; public float PlayerX; }
    }
}
