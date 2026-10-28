// Core2RunEvents.cs — the events a Core 2 run meets on fogged nodes, their quest gates, and the run-level effects their
// choices apply.
//
// JS at the pin:
//   src/content/events.js       eventChoicesWithHistory (stable choice ids, per-choice history requirements)
//   src/model/quests.js         eventChoiceRequirementMet (all / any / none over eventChoice history rows),
//                               availableEventChoices, recordEventChoice, recordQuestCompletion, questsCompletedBy
//   src/engine/quests.js        choiceAffordable (requires.cinders), commitEventChoice (effects, then the history row,
//                               then the quest completion check), completeQuest
//   src/engine/actions.js       executeRunEffects / createRunContext / runRunOpcode: addCinders, addCardToDeck,
//                               removeCardFromDeck, upgradeCard, addRelic, loseMaxHpPct, startCombat, swapClass, and heal /
//                               damage on the run's facade; an effect's `if` (random pct on `misc`) is read first
//
// A Core 2 class stays fixed for the run: JS swapRunClass throws for a configured class selection, so the Turncoat's
// Mirror "Step through" choice cannot be committed (Core2RunEvents.Runnable reports it; the JS build offers it and errors).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2EventChoice
    {
        public int Index { get; set; }
        public string Id { get; set; }
        public string Label { get; set; }
        public string ResultText { get; set; }
        public JObject Requires { get; set; }
        public JObject RequiresHistory { get; set; }
        public JArray Effects { get; set; }
    }

    public sealed class Core2EventReceipt
    {
        public string EventId { get; set; }
        public string ChoiceId { get; set; }
        public List<string> Completions { get; } = new List<string>();
        /// <summary>The encounter a startCombat effect opened (the fight follows the result text).</summary>
        public string Encounter { get; set; }
    }

    public static class Core2RunEvents
    {
        private static JObject Table(Core2Content content) => (JObject)content.Table("adapter/events.json");

        /// <summary>JS eventChoicesWithHistory: the authored choices with their stable ids and history requirements.</summary>
        public static List<Core2EventChoice> Choices(Core2Content content, string eventId)
        {
            var table = Table(content);
            var def = content.Events.First(e => e.Id == eventId).Raw;
            var choices = ((JArray)def["choices"]).OfType<JObject>().ToList();
            var ids = Core2Json.Strings(table["eventChoiceIds"]?[eventId]);
            if (ids.Count != choices.Count) return new List<Core2EventChoice>();
            var requirements = table["eventChoiceHistoryRequirements"]?[eventId] as JArray;
            return choices.Select((choice, i) => new Core2EventChoice
            {
                Index = i, Id = ids[i], Label = (string)choice["label"], ResultText = (string)choice["resultText"], Requires = choice["requires"] as JObject,
                RequiresHistory = requirements != null && i < requirements.Count ? requirements[i] as JObject : null, Effects = (JArray)choice["effects"] ?? new JArray(),
            }).ToList();
        }

        private static bool HasChoice(IReadOnlyList<JObject> history, JObject reference) => history.Any(row => (string)row["kind"] == "eventChoice"
            && (string)row["eventId"] == (string)reference["eventId"] && (string)row["choiceId"] == (string)reference["choiceId"]);

        /// <summary>JS eventChoiceRequirementMet: every `all`, at least one `any`, no `none` (absent groups are neutral).</summary>
        public static bool RequirementMet(JObject requirement, IReadOnlyList<JObject> history)
        {
            if (requirement == null) return true;
            history ??= new List<JObject>();
            if (requirement["any"] is JArray any && any.Count == 0) return false;
            if (((requirement["all"] as JArray) ?? new JArray()).OfType<JObject>().Any(r => !HasChoice(history, r))) return false;
            if (requirement["any"] is JArray anyRefs && !anyRefs.OfType<JObject>().Any(r => HasChoice(history, r))) return false;
            if (((requirement["none"] as JArray) ?? new JArray()).OfType<JObject>().Any(r => HasChoice(history, r))) return false;
            return true;
        }

        /// <summary>JS choiceAffordable: a choice's cinder price.</summary>
        public static bool Affordable(Core2EventChoice choice, Core2Run run) => !(choice.Requires?["cinders"] is JValue price) || run.Cinders >= (int)price;

        /// <summary>Whether a Core 2 run can commit the choice's effects (swapClass refuses: a configured class is fixed).</summary>
        public static bool Runnable(Core2EventChoice choice) => !choice.Effects.OfType<JObject>().Any(e => (string)e["op"] == "swapClass");

        /// <summary>JS availableEventChoices: the choices the run's history opens, in authored order.</summary>
        public static List<Core2EventChoice> Available(Core2Content content, Core2Run run, string eventId) =>
            Choices(content, eventId).Where(c => RequirementMet(c.RequiresHistory, run.History)).ToList();

        /// <summary>JS commitEventChoice: the effects, then the history row, then any quest it completes.</summary>
        public static Core2EventReceipt Commit(Core2RunData data, Core2Run run, Core2Rng rng, string eventId, string choiceId)
        {
            var choice = Choices(data.Content, eventId).FirstOrDefault(c => c.Id == choiceId)
                ?? throw new InvalidOperationException("commitEventChoice: event '" + eventId + "' has no choice '" + choiceId + "'");
            if (!RequirementMet(choice.RequiresHistory, run.History)) throw new InvalidOperationException("commitEventChoice: '" + eventId + "/" + choiceId + "' is not open to this run's history");
            if (!Affordable(choice, run)) throw new InvalidOperationException("commitEventChoice: '" + eventId + "/" + choiceId + "' costs more than the run holds");
            var receipt = new Core2EventReceipt { EventId = eventId, ChoiceId = choiceId };
            receipt.Encounter = Execute(data, run, rng, choice.Effects);
            run.History.Add(new JObject { ["kind"] = "eventChoice", ["eventId"] = eventId, ["choiceId"] = choiceId, ["actNumber"] = run.ActNumber, ["floor"] = run.Floor,
                ["mapNodeId"] = run.MapNodeId == null ? JValue.CreateNull() : (JToken)run.MapNodeId });
            foreach (var quest in ((JObject)Table(data.Content)["questChains"] ?? new JObject()).Properties())
            {
                var completes = ((quest.Value["completes"] as JArray) ?? new JArray()).OfType<JObject>().Any(r => (string)r["eventId"] == eventId && (string)r["choiceId"] == choiceId);
                if (!completes) continue;
                if (run.History.Any(h => (string)h["kind"] == "questCompleted" && (string)h["questId"] == quest.Name)) continue;
                run.History.Add(new JObject { ["kind"] = "questCompleted", ["questId"] = quest.Name, ["source"] = "event" });
                receipt.Completions.Add(quest.Name);
            }
            return receipt;
        }

        /// <summary>JS executeRunEffects for an event choice. Returns the encounter a startCombat effect stored, or null.</summary>
        public static string Execute(Core2RunData data, Core2Run run, Core2Rng rng, JArray effects)
        {
            string encounter = null;
            var queue = new Queue<JObject>(effects.OfType<JObject>());
            var idCounter = 0;
            var guard = 0;
            while (queue.Count > 0)
            {
                if (++guard > 1000) throw new InvalidOperationException("Run effect queue did not drain");
                var effect = queue.Dequeue();
                if (effect["if"] is JObject predicate && !Predicate(rng, predicate)) continue;
                switch ((string)effect["op"])
                {
                    case "addCinders":
                    {
                        var n = Number(run, effect["amount"]);
                        run.Cinders = Math.Max(0, run.Cinders + (int)Math.Floor(n));
                        break;
                    }
                    case "addCardToDeck":
                    {
                        var cardId = (string)effect["card"];
                        data.Content.Card(cardId);
                        var owned = new HashSet<string>(run.Owned.Select(c => c.InstanceId), StringComparer.Ordinal);
                        string id;
                        do id = "run" + (++idCounter); while (owned.Contains(id));
                        run.Deck.Add(new Core2RunCard { InstanceId = id, CardId = cardId });
                        Core2RunRewards.RecordCollectedCards(data, run);
                        break;
                    }
                    case "removeCardFromDeck":
                    {
                        var index = -1;
                        if (effect["card"] != null) index = run.Deck.FindIndex(c => c.CardId == (string)effect["card"] && c.GrantedBy == null);
                        else if ((bool?)effect["random"] == true)
                        {
                            var candidates = Enumerable.Range(0, run.Deck.Count).Where(i => run.Deck[i].GrantedBy == null).ToList();
                            index = candidates.Count > 0 ? candidates[(int)Math.Floor(rng.Float("misc") * candidates.Count)] : -1;
                        }
                        if (index >= 0) { Core2RunRewards.RecordCollectedCards(data, run); run.Deck.RemoveAt(index); }
                        break;
                    }
                    case "upgradeCard":
                    {
                        var candidates = Core2RunRest.SmithPlan(data, run).Where(c => c.ItemKind != "relic" && Core2RunRest.ChangesDeckCards(data, run, c)).ToList();
                        if (effect["card"] != null) candidates = candidates.Where(c => Core2RunRest.ChangedCardIds(data, run, c).Contains((string)effect["card"])).ToList();
                        if (candidates.Count == 0) break;
                        var chosen = (bool?)effect["random"] == true ? rng.Pick("misc", candidates) : candidates[0];
                        Core2RunRest.CommitUpgrade(data, run, chosen.ItemRef);
                        break;
                    }
                    case "addRelic":
                    {
                        var relicId = (string)effect["id"];
                        if (relicId == null && (bool?)effect["random"] == true)
                        {
                            var pool = data.Content.Relics.Where(r => !run.Relics.Contains(r.Id) && ((string)r.Raw["pool"] ?? "reward") == "reward").Select(r => r.Id).ToList();
                            if (pool.Count == 0) break;
                            relicId = rng.Pick("relicRewards", pool);
                        }
                        if (relicId != null && !run.Relics.Contains(relicId)) { data.Relic(relicId); run.Relics.Add(relicId); }
                        break;
                    }
                    case "loseMaxHpPct":
                    {
                        var pct = Number(run, effect["pct"]);
                        var before = run.MaxHp;
                        run.MaxHp = Math.Max(1, (int)Math.Floor(run.MaxHp * (1 - pct / 100)));
                        run.MaxHpAdjustment += run.MaxHp - before;
                        run.Hp = Math.Min(run.Hp, run.MaxHp);
                        break;
                    }
                    case "startCombat":
                        data.Encounter((string)effect["encounterId"]);
                        encounter = (string)effect["encounterId"];
                        break;
                    case "heal":
                    {
                        var amount = Math.Max(0, (int)Math.Floor(Number(run, effect["amount"])));
                        run.Hp = Math.Min(run.MaxHp, run.Hp + amount);
                        break;
                    }
                    case "damage":
                    {
                        var hits = effect["hits"] == null ? 1 : Math.Max(0, (int)Math.Floor(Number(run, effect["hits"])));
                        for (var h = 0; h < hits; h++) run.Hp = Math.Max(0, run.Hp - Math.Max(0, (int)Math.Floor(Number(run, effect["amount"]))));
                        break;
                    }
                    case "loseHp":
                        run.Hp = Math.Max(0, run.Hp - Math.Max(0, (int)Math.Floor(Number(run, effect["amount"]))));
                        break;
                    case "restoreMana":
                        run.Mana = Math.Min(run.MaxMana, run.Mana + Math.Max(0, (int)Math.Floor(Number(run, effect["amount"]))));
                        break;
                    case "addFlask":
                    {
                        var flaskId = (string)effect["id"];
                        if (flaskId == null && (bool?)effect["random"] == true) flaskId = rng.Pick("flaskRewards", data.Content.Flasks.Select(f => f.Id).ToList());
                        if (flaskId != null) Core2RunRewards.AcquireFlask(data, run, flaskId);
                        break;
                    }
                    case "swapClass":
                    {
                        // JS rolls the random class on `misc`, then swapRunClass refuses a configured class selection.
                        if ((bool?)effect["random"] == true) { var others = data.Content.Classes.Select(c => c.Id).Where(id => id != run.Class).ToList(); if (others.Count > 0) rng.Pick("misc", others); }
                        throw new InvalidOperationException("Configured classes stay fixed for the run; choose an unlocked class when starting a new run");
                    }
                    default:
                        throw new InvalidOperationException("Run opcode '" + (string)effect["op"] + "' has no Core 2 implementation");
                }
            }
            return encounter;
        }

        /// <summary>The run-level predicates event effects use (JS evalPredicate: random pct reads `misc`).</summary>
        private static bool Predicate(Core2Rng rng, JObject predicate)
        {
            switch ((string)predicate["p"])
            {
                case "random": return rng.Float("misc") * 100 < (double)predicate["pct"];
                case "all": return ((JArray)predicate["preds"]).OfType<JObject>().All(p => Predicate(rng, p));
                case "any": return ((JArray)predicate["preds"]).OfType<JObject>().Any(p => Predicate(rng, p));
                case "not": return !Predicate(rng, (JObject)predicate["pred"]);
                default: throw new InvalidOperationException("Run predicate '" + (string)predicate["p"] + "' has no Core 2 implementation");
            }
        }

        /// <summary>A run-level amount: a number, or a formula over the run's facade (percentMaxHp, missingHp).</summary>
        private static double Number(Core2Run run, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) return (double)token;
            var node = (JObject)token;
            switch ((string)node["f"])
            {
                case "percentMaxHp": return run.MaxHp * (double)node["pct"] / 100;
                case "missingHp": return Math.Max(0, run.MaxHp - run.Hp);
                case "missingMana": return Math.Max(0, run.MaxMana - run.Mana);
                case "add": return ((JArray)node["args"]).Sum(a => Number(run, a));
                case "mul": return ((JArray)node["args"]).Aggregate(1.0, (acc, a) => acc * Number(run, a));
                default: throw new InvalidOperationException("Run formula '" + (string)node["f"] + "' has no Core 2 implementation");
            }
        }
    }
}
