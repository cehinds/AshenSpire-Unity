// Core2FightRunHooks.cs — what the run layer asks of a live Core 2 fight beyond card play and End Turn: drinking a flask,
// and settling a fight as won without playing it (the auto-resolved oracle runs; a debug skip). A partial of Core2Fight
// kept with the run layer so the combat files stay as phases 5 and 7 own them.
//
// JS at the pin:
//   src/engine/combat.js     doUseFlask (player turn only; a charge flask spends a charge, a utility flask leaves its
//                            slot; flaskUsed; the flask's effects queued with flaskPowerMult as the amount scale)
//   src/content/scripts.js   wondrousDraught — hand port of the one scripted JS effect a Core 2 run reaches: two different
//                            random flasks (not itself, no scripted flask) picked on `misc`, their effects queued in turn
//   src/engine/actions.js    evalNum's amountMult (flaskPowerMult): every amount rounded up after the scale
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        /// <summary>
        /// Settle the fight as won before it is played: every enemy defeated (HP 0), the result victory. The fight was
        /// built for real (its setup, HP rolls, shuffle and first plans drew on the run's RNG), so the run continues exactly
        /// as JS does for a fight whose enemies all fall (tools/js-core2-run.mjs scripted-runs-auto).
        /// </summary>
        public void SettleAsWon()
        {
            if (Result != null) throw new InvalidOperationException("The fight has already ended");
            foreach (var enemy in Enemies) { enemy.Hp = 0; enemy.Alive = false; }
            Result = "victory";
        }

        /// <summary>
        /// JS doUseFlask: the flask's effects (wondrousDraught expanded), each amount scaled by flaskPowerMult and rounded up,
        /// at the target (a targeted flask without one aims at the first living enemy). The caller spends the charge or slot.
        /// </summary>
        public void UseFlask(JObject flask, double amountMult = 1, string targetId = null)
        {
            if (Phase != "player" || Result != null) throw new InvalidOperationException("Flasks can only be used on the player turn");
            RequireNoCardChoice();
            var p = Player;
            Core2Combatant target = null;
            if (targetId != null)
            {
                target = Find(targetId);
                if (target == null || !target.Alive) throw new InvalidOperationException("Invalid target '" + targetId + "'");
            }
            else if ((bool?)flask["targeted"] == true) target = Enemies.FirstOrDefault(e => e.Alive);
            Emit("flaskUsed", new JObject { ["flaskId"] = flask["id"], ["targetId"] = target?.Id });
            foreach (var effect in ((JArray)flask["effects"] ?? new JArray()).OfType<JObject>())
            {
                if (effect["script"]?.Type == JTokenType.String)
                {
                    if ((string)effect["script"] != "wondrousDraught") throw new NotSupportedException("Flask script '" + (string)effect["script"] + "' is not ported");
                    // scripts.js wondrousDraught: two different random flasks, none of them scripted.
                    bool Scripted(JObject def) => ((JArray)def["effects"] ?? new JArray()).OfType<JObject>().Any(e => e["script"]?.Type == JTokenType.String);
                    var pool = Content.Flasks.Select(f => f.Raw).Where(f => (string)f["id"] != "wondrousDraught" && !Scripted(f)).ToList();
                    if (pool.Count == 0) continue;
                    var first = Rng.Pick("misc", pool);
                    var rest = pool.Where(f => (string)f["id"] != (string)first["id"]).ToList();
                    var picks = rest.Count > 0 ? new List<JObject> { first, Rng.Pick("misc", rest) } : new List<JObject> { first };
                    foreach (var pick in picks)
                        foreach (var inner in ((JArray)pick["effects"] ?? new JArray()).OfType<JObject>()) Enqueue(FlaskAction(inner, amountMult, p, target));
                    continue;
                }
                Enqueue(FlaskAction(effect, amountMult, p, target));
            }
            DrainQueue();
        }

        private Core2FightAction FlaskAction(JObject effect, double amountMult, Core2Combatant player, Core2Combatant target)
        {
            var scaled = (JObject)effect.DeepClone();
            foreach (var key in new[] { "amount", "stacks" })
            {
                var value = scaled[key];
                if (value == null) continue;
                if (amountMult != 1 && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)) scaled[key] = (int)Math.Ceiling(Math.Floor((double)value) * amountMult);
                else if (value is JObject formula && (string)formula["f"] == "percentMaxHp")
                {
                    // evalRaw: the unfloored percentage (the HP flask's heal), scaled and rounded up under a flask power
                    // multiplier; the heal then floors it once. Resolved here because the fight's formula set lacks it.
                    var of = (string)formula["of"] == "target" ? target ?? player : player;
                    var raw = of.MaxHp * (double)formula["pct"] / 100;
                    scaled[key] = amountMult != 1 ? (JToken)(int)Math.Ceiling(raw * amountMult) : raw;
                }
            }
            return new Core2FightAction { Effect = scaled, Source = player, Owner = player, Target = target ?? player };
        }
    }
}
