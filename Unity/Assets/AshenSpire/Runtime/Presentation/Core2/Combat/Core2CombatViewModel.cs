// Core2CombatViewModel.cs — builds the Core 2 combat screen's model from the run session, the fight, the display state
// and the screen's own choices. Every rule it shows is the domain's (Core2Fight, Core2Knowledge, Core2EnemyInspector,
// Core2FightPreview); this file only chooses words, order and art keys, as the JS components do.
//
// JS at the pin:
//   src/ui/models/CombatIntentModel.js     combatIntentPresentation, intentDamageRead (title, parts, "Total 0–12 HP")
//   src/ui/models/CombatHealthModel.js     combatHealthModel (badge order: Block, Evade, Precision, Poise, Ward; the HP
//                                          bar's guard tone and gold Poise trim)
//   src/ui/components/combatantOverhead.js combatantIntent (the overhead read), intentParts
//   src/ui/components/combatActionRow.js   the footer (SP orb, DRAW, Discard/Exhaust, End Turn · E · HOLD, Potions)
//   src/ui/components/combatTools.js       the Reaction and Auto switches ("enabled"/"disabled")
//   src/ui/components/reactionChoiceModal.js openReactionChoiceModal, committedReactionStack, reactionStackEntryName
//   src/ui/components/discardChoiceModal.js  openDiscardChoiceModal as screens/combat.js showPendingDiscard opens it for a
//                                          pendingCardChoice (title, prompt, Confirm selection, Choose later, ordered picks)
//   src/ui/components/cardChoiceModal.js   openCardChoiceModal over model/cardChoices.js cardChoice (the maneuver choice)
//   src/ui/components/combatantInspector.js + models/CombatantInspectorSections.js the enemy inspector's sections
//   src/model/secondbeat.js (endTurn row)  End Turn holds while a card is still playable, else a tap ends the turn
//   src/model/environmentArt.js            regionForRun (the seat's region), the scenery index hash
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>Everything one paint reads.</summary>
    public sealed class Core2CombatViewInputs
    {
        public Core2RunSession Session { get; set; }
        public Core2Fight Fight { get; set; }
        /// <summary>The numbers to show (a beat's display state, or a snapshot of the fight when idle).</summary>
        public Core2CombatDisplay Display { get; set; }
        /// <summary>The armed card (JS selected), or null.</summary>
        public string Selected { get; set; }
        /// <summary>Beats are playing: no command but answering is accepted, and the hand shows the display's cards.</summary>
        public bool Busy { get; set; }
        public bool Auto { get; set; }
        public string ReactionSelected { get; set; }
        /// <summary>A utility flask waits for an enemy target.</summary>
        public bool FlaskArmed { get; set; }
        /// <summary>Card id → card painting resource (Resources/Core2Ui/card-art.json).</summary>
        public IReadOnlyDictionary<string, string> CardArt { get; set; }
        /// <summary>The chosen-card door is open (a pending choice the player has not put off with Choose later).</summary>
        public bool CardChoiceOpen { get; set; }
        /// <summary>The door's picks, in pick order.</summary>
        public IReadOnlyList<string> CardChoiceSelected { get; set; }
        /// <summary>The hand card whose maneuver choice is open, or null.</summary>
        public string ManeuverPick { get; set; }
        /// <summary>A card choice put off with Choose later: End Turn stays live to reopen its door.</summary>
        public bool CardChoiceDeferred { get; set; }
    }

    public static class Core2CombatViewModel
    {
        public const string EnemyArt = "Art/js1815/combat/enemies/";
        public const string PlayerArt = "Art/js1815/combat/player/";
        public const string IconArt = "Art/js1815/combat/icons/";
        public const string RelicArt = "Art/js1815/combat/relics/";
        public const string SceneArt = "Art/js1815/combat/scenes/";
        /// <summary>JS RANGES by engagement band (0 melee, 1 reach, 2 ranged).</summary>
        public static readonly string[] Lanes = { "melee", "reach", "ranged" };

        /// <summary>The status icons the imported pack carries (Art/js1815/combat/icons/status-{id}).</summary>
        private static readonly HashSet<string> StatusIcons = new HashSet<string>(StringComparer.Ordinal)
        {
            "bleed", "block", "burn", "cinders", "dexterity", "frail", "frost", "health", "insanity", "madness", "mana", "poise", "regen",
            "staggered", "stamina", "strength", "vulnerable", "weak",
        };

        /// <summary>JS ACTION_SIGILS (cardTagSymbols / illustrated-reference sigils): the type band's word and mark per action.</summary>
        private static readonly Dictionary<string, (string Label, string Mark)> Sigils = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["attack"] = ("Attack", "Art/js1815/cards/mark-edbdf1f7033cec21"),
            ["defend"] = ("Defense", "Art/js1815/cards/mark-621e6f898d1dab32"),
            ["evade"] = ("Evade", "Art/js1815/cards/mark-dc3442c9ca1d09aa"),
            ["counter"] = ("Counter", "Art/js1815/cards/mark-61c1e9521dceaafb"),
            ["sweep"] = ("Sweep", "Art/js1815/cards/mark-7e78fa07ca0f2171"),
            ["ranged"] = ("Ranged", "Art/js1815/cards/mark-8e99100c5988d2e4"),
            ["smash"] = ("Smash", "Art/js1815/cards/mark-e9985a7247ed53d5"),
            ["spell"] = ("Spell", "Art/js1815/cards/mark-85fc58512696d766"),
            ["power"] = ("Power", "Art/js1815/cards/mark-ff2f9c5f9afdeb1e"),
            ["skill"] = ("Skill", "Art/js1815/cards/mark-6d41b984500e4766"),
            ["status"] = ("Status", "Art/js1815/cards/mark-a0e30e4aa1876871"),
        };

        // ================================================================================================================
        // The screen
        // ================================================================================================================

        public static Core2CombatScreenModel Build(Core2CombatViewInputs i)
        {
            var fight = i.Fight;
            var display = i.Display ?? Core2CombatDisplay.Snapshot(fight);
            var run = i.Session?.Run;
            var model = new Core2CombatScreenModel
            {
                PlayerTurn = !display.EnemySide, TurnRibbon = Core2CombatText.TurnRibbon(display.EnemySide), Result = fight.Result,
                Backdrop = run == null ? SceneArt + "pale-marches-1" : Backdrop(i.Session),
                Targeting = i.Selected != null && fight.CardTargetsEnemy(i.Selected) ? i.Selected : null,
                InputLocked = i.Busy || fight.PendingReaction != null || fight.HasCardChoice || i.ManeuverPick != null || fight.Result != null || fight.Phase != "player",
            };
            var player = display[fight.Player.Id];
            model.Top = new Core2CombatTopBand
            {
                Hp = player.Hp, MaxHp = player.MaxHp, HpText = player.Hp + "/" + player.MaxHp, Cinders = run?.Cinders ?? 0,
                Relics = fight.Player.RelicIds.Select(id => Relic(fight.Content, id)).ToList(),
            };
            model.Player = Fighter(fight, fight.Player, player, null);
            model.Player.Figure = PlayerArt + (fight.Player.ClassId ?? run?.Class ?? "reaver") + "-ready";
            foreach (var enemy in fight.Enemies)
            {
                var row = Fighter(fight, enemy, display[enemy.Id], i.Busy ? null : model.Targeting);
                if (row.Alive && !i.Busy) row.Intent = Intent(fight, enemy);
                if (i.FlaskArmed && row.Alive && !i.Busy) row.Targetable = true;
                model.Enemies.Add(row);
            }
            model.Hand = Hand(fight, display, i);
            model.Footer = Footer(i, display, model.Hand);
            // JS showPendingReaction waits while a card choice is pending: the chosen-card door comes first.
            if (fight.HasCardChoice && !i.Busy && fight.Result == null) { if (i.CardChoiceOpen) model.CardChoice = CardChoice(fight, i.CardChoiceSelected); }
            else if (fight.PendingReaction != null && !i.Busy && fight.Result == null) model.Reaction = Reaction(fight, i.ReactionSelected);
            if (i.ManeuverPick != null && !i.Busy) model.ManeuverChoice = ManeuverChoice(fight, i.ManeuverPick);
            return model;
        }

        // ================================================================================================================
        // The card choices
        // ================================================================================================================

        /// <summary>
        /// JS showPendingDiscard for a pendingCardChoice → openDiscardChoiceModal: the resolving card's name, "Choose {count}
        /// cards", "Choose cards from {from} for {to}. …" (JS passes effect.to as it is, so a modifyCards choice, which names no
        /// destination, reads "for undefined"), Confirm selection enabled at exactly count picks, Choose later to close.
        /// </summary>
        public static Core2CardChoiceModalModel CardChoice(Core2Fight fight, IReadOnlyList<string> selected)
        {
            var pending = fight.PendingCardChoice;
            if (pending == null) return null;
            var effect = pending.Effect;
            var from = (string)effect["from"];
            var to = (string)effect["to"];
            var model = new Core2CardChoiceModalModel
            {
                Eyebrow = fight.Content.HasCard(pending.CardId) ? fight.Content.Card(pending.CardId).Name : pending.CardId,
                Title = Core2CombatText.T("combat.cards.choose.title", ("count", pending.Count)),
                Prompt = Core2CombatText.T("combat.cards.choose.prompt", ("from", from ?? "undefined"), ("to", to ?? "undefined")),
                ConfirmLabel = Core2CombatText.T("combat.cards.choose.confirm"), CloseLabel = Core2CombatText.T("combat.discard.later"),
                Count = pending.Count, Ordered = from == to,
            };
            foreach (var card in pending.Candidates)
            {
                var name = fight.Content.HasCard(card.CardId) ? fight.Content.Card(card.CardId).Name : card.CardId;
                var row = new Core2ReactionOptionModel { Id = card.InstanceId, Name = name, CardId = card.CardId, InstanceId = card.InstanceId };
                try { row.Summary = string.Join(" · ", FaceLines(fight, card)); }
                catch (Exception) { row.Summary = ""; } // JS: a summary that throws leaves a name-only tile
                model.Options.Add(row);
            }
            if (selected != null) model.Selected = selected.Where(id => pending.CandidateIds.Contains(id)).Distinct().Take(pending.Count).ToList();
            model.ConfirmEnabled = model.Selected.Count == pending.Count;
            return model;
        }

        /// <summary>
        /// JS playCard → openCardChoiceModal({ plan: cardChoicePlan }): the card's name as the eyebrow, "Choose a stance", one
        /// tile per maneuver ("Attack" … "Evade", its id as the line; JS marks the stance already held " (active)" and disables
        /// it, which a Core 2 maneuver never is: the maneuver form leaves player.stanceId unset), and Cancel.
        /// </summary>
        public static Core2ManeuverChoiceModel ManeuverChoice(Core2Fight fight, string instanceId)
        {
            var instance = fight.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            if (instance == null) return null;
            IReadOnlyList<string> options;
            try { options = fight.CardChoiceOptions(instanceId); }
            catch (InvalidOperationException) { return null; }
            if (options == null) return null;
            var name = fight.Content.Card(instance.CardId).Name;
            var model = new Core2ManeuverChoiceModel
            {
                InstanceId = instanceId, Eyebrow = name, Title = Core2CombatText.T("cardChoice.stanceTitle"),
                CloseLabel = Core2CombatText.T("common.cancel") + " " + name, CancelLabel = Core2CombatText.T("common.cancel"),
            };
            foreach (var id in options) model.Options.Add(new Core2ManeuverOptionModel { Id = id, Name = Core2CombatText.Capitalize(id), Text = id });
            return model;
        }

        /// <summary>JS combatEnvironment without a layered presentation: the seat's region, its scenery picked by seed and floor.</summary>
        public static string Backdrop(Core2RunSession session)
        {
            var run = session.Run;
            string region = null;
            if (run.SeatOrder != null && run.SeatOrder.Count > 0)
            {
                var seat = Core2ActMap.SeatAt(run);
                var row = session.Content.Seats.FirstOrDefault(s => s.Id == seat);
                region = (string)row?.Raw["regionId"];
            }
            var regions = new[] { "hollow-weald", "pale-marches", "cinder-reach" };
            if (!regions.Contains(region)) region = regions[(int)((Hash(run.Seed.ToString(CultureInfo.InvariantCulture)) % 3 + (uint)((Math.Max(1, run.ActNumber) - 1) % 3)) % 3)];
            var scenes = new[] { "1", "3" };
            var index = (Hash(run.Seed.ToString(CultureInfo.InvariantCulture) + ":scenery") + (uint)Math.Max(0, run.Floor)) % (uint)scenes.Length;
            return SceneArt + region + "-" + scenes[index];
        }

        /// <summary>JS environmentArt.js hash (FNV-1a over UTF-16 code units).</summary>
        public static uint Hash(string text)
        {
            uint value = 2166136261;
            foreach (var c in text ?? "") value = unchecked((value ^ c) * 16777619);
            return value;
        }

        private static Core2RelicChip Relic(Core2Content content, string id)
        {
            var row = content.Relics.FirstOrDefault(r => r.Id == id);
            return new Core2RelicChip { Id = id, Name = (string)row?.Raw["name"] ?? Core2CombatText.Words(id), Text = (string)row?.Raw["textTemplate"], Art = RelicArt + id };
        }

        // ================================================================================================================
        // Fighters: HP bar, badges, statuses
        // ================================================================================================================

        public static Core2CombatantModel Fighter(Core2Fight fight, Core2Combatant entity, Core2DisplayFighter shown, string targeting)
        {
            shown ??= Core2CombatDisplay.Snapshot(fight)[entity.Id];
            var isPlayer = entity.Side == Core2Side.Player;
            var model = new Core2CombatantModel
            {
                Id = entity.Id, IsPlayer = isPlayer, Alive = shown.Alive, Hp = shown.Hp, MaxHp = shown.MaxHp,
                Name = isPlayer ? Core2CombatLog.PlayerName : fight.EnemyDef(entity).Name,
                HpText = shown.Hp + "/" + shown.MaxHp, HpFraction = shown.MaxHp > 0 ? Math.Max(0, Math.Min(1, shown.Hp / (double)shown.MaxHp)) : 0,
                Guard = shown.Barrier > 0 ? "barrier" : shown.Block > 0 ? "block" : shown.Ward > 0 ? "ward" : "none",
                Poised = shown.Poise > 0, Stance = shown.Stance,
                Figure = isPlayer ? null : EnemyArt + entity.EnemyId + "-idle",
            };
            model.PoiseLabel = model.Poised ? Core2CombatText.T("combat.protection.poiseValue", ("value", shown.Poise), ("max", Math.Max(shown.Poise, shown.PoiseMax))) : null;
            model.Badges = Badges(shown);
            model.Statuses = Statuses(fight.Content, shown);
            if (!isPlayer && fight.Range != null)
            {
                try { model.Lane = Lanes[Math.Max(0, Math.Min(2, fight.Range.Band(Core2Range.ActorKey(fight.Player), Core2Range.ActorKey(entity))))]; }
                catch (InvalidOperationException) { model.Lane = null; }
            }
            if (!isPlayer && targeting != null && entity.Alive)
            {
                model.Targetable = true;
                var preview = fight.PreviewCardHit(targeting, entity.Id);
                if (preview != null)
                {
                    model.HitBadge = preview.HitChance + "%";
                    model.HitTone = preview.Mode == "advantage" ? "advantage" : preview.Mode == "disadvantage" ? "disadvantage" : "neutral";
                }
            }
            return model;
        }

        /// <summary>JS combatHealthModel badges, in the build's order: Block, Evade, Precision, Poise, Ward (each only while above 0).</summary>
        public static List<Core2BadgeModel> Badges(Core2DisplayFighter shown)
        {
            var badges = new List<Core2BadgeModel>();
            void Add(string kind, string label, int value) { if (value > 0) badges.Add(new Core2BadgeModel { Kind = kind, Label = label, Value = value }); }
            Add("shield", Core2CombatText.T("combat.protection.block"), shown.Block);
            Add("evade", Core2CombatText.T("card.tags.evade"), shown.Stacks("evade"));
            Add("precision", Core2CombatText.T("card.tags.precision"), shown.Stacks("precision"));
            Add("poise", Core2CombatText.T("combat.protection.poise"), shown.Poise);
            Add("ward", Core2CombatText.T("combat.protection.ward"), shown.Ward + shown.Barrier);
            return badges;
        }

        /// <summary>The status chips: every active status but the two the badges carry (Evade, Precision), in id order.</summary>
        public static List<Core2StatusChip> Statuses(Core2Content content, Core2DisplayFighter shown)
        {
            var rows = content.Statuses;
            return shown.Statuses.Where(p => p.Value > 0 && p.Key != "evade" && p.Key != "precision").OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            {
                var row = rows.FirstOrDefault(s => s.Id == p.Key);
                return new Core2StatusChip
                {
                    Id = p.Key, Name = (string)row?.Raw["name"] ?? Core2CombatText.Words(p.Key), Stacks = p.Value, Tooltip = (string)row?.Raw["tooltip"],
                    Icon = IconArt + "status-" + (StatusIcons.Contains(p.Key) ? p.Key : "unknown"),
                };
            }).ToList();
        }

        // ================================================================================================================
        // Intent (JS combatIntentPresentation + intentDamageRead)
        // ================================================================================================================

        public static Core2IntentBadge Intent(Core2Fight fight, Core2Combatant enemy)
        {
            var view = Core2Knowledge.Project(enemy, fight.Turn);
            var intent = enemy.Intent ?? new Core2Intent { Kind = "unknown" };
            var badge = new Core2IntentBadge();
            badge.FollowUps = view.Hidden ? new List<string>() : Core2Knowledge.RemainingFollowUps(fight, enemy, (enemy.PlannedMoves ?? new List<Core2PlannedMove>()).Select(m => m.MoveId).ToList())
                .Select(s => s.Kind == "action" ? IconFor(s.Stance) : "unknown").ToList();
            // A failed read (knowledgeRead unknown) names nothing about the move.
            if (view.Hidden) { badge.Visibility = "unknown"; badge.Title = "?"; badge.Stance = "unknown"; return badge; }
            if (intent.Kind == "staggered")
            {
                badge.Visibility = "partial"; badge.Title = Core2CombatText.T("combat.intent.partial", ("stance", "Preparing")); badge.Stance = "preparing";
                badge.Parts.Add(new Core2IntentPart { Icon = "prepare" });
                return badge;
            }
            var stance = intent.Profile?.Maneuver == "evade" ? "evading" : view.Stance ?? "preparing";
            if (string.IsNullOrEmpty(intent.Kind) || intent.Kind == "unknown" || intent.MoveId == null)
            {
                badge.Visibility = "unknown"; badge.Title = "?"; badge.Stance = "unknown"; return badge;
            }
            var family = intent.Kind == "buff" ? "buffing" : stance == "ranged" || stance == "sweeping" ? "attacking" : stance == "preparing" ? "casting" : stance;
            var read = DamageRead(fight, enemy, null);
            var damage = read?.Label ?? (intent.Damage != null ? (intent.Hits > 1 ? intent.Damage + "×" + intent.Hits : Core2CombatText.Number(intent.Damage.Value)) : null);
            Core2IntentPart Part(string icon, string value) => new Core2IntentPart { Icon = icon, Value = value };
            var block = intent.Block?.ToString(CultureInfo.InvariantCulture);
            switch (family)
            {
                case "countering":
                {
                    // JS counterParts: the Counter's Block, its return damage, and its Poise damage when it has one.
                    var (counterDamage, counterPoise) = CounterPayload(fight, enemy, intent.MoveId);
                    badge.Parts.Add(Part("counter", block));
                    if (!(counterPoise > 0) || counterDamage > 0) badge.Parts.Add(Part("attack", counterDamage?.ToString(CultureInfo.InvariantCulture)));
                    if (counterPoise > 0) badge.Parts.Add(Part("smash", counterPoise.Value.ToString(CultureInfo.InvariantCulture)));
                    break;
                }
                case "evading": badge.Parts.Add(Part("evade", Core2CombatText.Number(enemy.StatusStacks("evade")))); break;
                case "defending": badge.Parts.Add(Part("defend", block)); break;
                case "smashing": badge.Parts.Add(Part("smash", damage)); break;
                case "attacking": badge.Parts.Add(Part("attack", damage)); break;
                default: badge.Parts.Add(Part(family == "buffing" ? "buff" : "cast", family == "buffing" ? null : damage)); break;
            }
            if (read != null) foreach (var part in badge.Parts) if (part.Value == damage) part.Summary = read.Summary;
            badge.Visibility = "known"; badge.Title = Core2CombatText.Capitalize(family); badge.Stance = family;
            var camp = intent.Profile?.Camp;
            badge.DamageClass = camp == "spell" || camp == "spells" || camp == "magic" ? "magic" : family == "attacking" || family == "smashing" ? "physical" : null;
            badge.Tone = read?.Tone;
            return badge;
        }

        /// <summary>
        /// JS primeEnemyCounter's armed payload as the intent shows it (counterDamage / counterPoiseDamage): the move's
        /// counterDamage (else damage) through enemyMoveDamage, and its authored counter Poise damage or, with none and no
        /// Poise damage effect, the stance rules' fallback Poise (stanceRules.js enemyCounterPoiseDamage).
        /// </summary>
        private static (int? Damage, int? Poise) CounterPayload(Core2Fight fight, Core2Combatant enemy, string moveId)
        {
            var raw = fight.EnemyDef(enemy).Move(moveId)?.Raw;
            if (raw == null) return (null, null);
            var move = (JObject)raw.DeepClone();
            if (raw["counterDamage"] != null) move["damage"] = raw["counterDamage"].DeepClone();
            var damage = move["damage"] == null || move["damage"].Type == JTokenType.Null ? 0 : Core2EnemyMoves.EnemyMoveDamage(enemy, move) ?? 0;
            var authored = (int?)raw["counterPoiseDamage"] ?? 0;
            var effects = (raw["effects"] as JArray ?? new JArray()).OfType<JObject>();
            var poise = authored > 0 || effects.Any(e => (string)e["op"] == "poiseDamage") ? authored : fight.Rules.FallbackPoise;
            return (damage, poise);
        }

        public sealed class Core2DamageReadModel
        {
            public string Label { get; set; }
            public string Summary { get; set; }
            public string Tone { get; set; }
        }

        /// <summary>
        /// JS intentDamageRead: the prominent per-hit number ("0–12" for one hit with a projection, else "5×3") and its scoped
        /// summary — "Total {amount} HP" for the whole intent, or, for the next contact a reaction answers,
        /// "Hit {i}/{n} · next {amount} HP". Null for a hidden intent or one without damage.
        /// </summary>
        public static Core2DamageReadModel DamageRead(Core2Fight fight, Core2Combatant enemy, (int Index, int Count)? contact)
        {
            var intent = enemy?.Intent;
            if (intent?.Damage == null || Core2Knowledge.Project(enemy, fight.Turn).Hidden) return null;
            var projected = fight.ProjectIntent(enemy, contact.HasValue ? 1 : (int?)null);
            // Each hit's damage as it meets the player's defenses (JS previewDamageHits hitDamages).
            var damages = fight.IntentHitDamages(enemy, contact.HasValue ? 1 : (int?)null) ?? new List<int> { intent.Damage.Value };
            if (damages.Count == 0) damages.Add(intent.Damage.Value);
            var same = damages.All(d => d == damages[0]);
            // A contact read (JS captureEnemyIntent, damageScope contact) carries HP-loss bounds, not a projectedDamage:
            // the prominent number stays the hit's damage and the bounds go in the summary.
            var label = !contact.HasValue && damages.Count == 1 && projected != null ? projected.Label
                : same ? damages[0] + (damages.Count > 1 ? "×" + damages.Count : "")
                : damages.Count <= 3 ? string.Join("+", damages) : damages.Min() + "–" + damages.Max() + "×" + damages.Count;
            var hits = damages.Count;
            var bounds = projected != null ? (projected.Min == projected.Max ? Core2CombatText.Number(projected.Max ?? 0) : projected.Min + "–" + projected.Max)
                : Core2CombatText.Number(intent.Damage.Value * hits);
            var basis = Core2CombatText.T(projected != null ? "combat.intent.damageHp" : "combat.intent.damageBeforeBlock");
            var summary = contact.HasValue
                ? Core2CombatText.T("combat.intent.damageContact", ("index", contact.Value.Index + 1), ("count", contact.Value.Count), ("amount", bounds), ("basis", basis))
                : Core2CombatText.T("combat.intent.damageTotal", ("amount", bounds), ("basis", basis));
            return new Core2DamageReadModel { Label = label, Summary = summary, Tone = projected?.Tone };
        }

        /// <summary>JS intentIcon for a stance family (follow-up symbols and the player's own action glyph).</summary>
        public static string IconFor(string stance)
        {
            switch (stance)
            {
                case "attacking": case "ranged": case "sweeping": case "attack": case "ranged attack": return "attack";
                case "smashing": case "smash": return "smash";
                case "defending": case "defend": return "defend";
                case "countering": case "counter": return "counter";
                case "evading": case "evade": return "evade";
                case "casting": return "cast";
                case "buffing": return "buff";
                case "sweep": return "sweep";
                default: return "prepare";
            }
        }

        // ================================================================================================================
        // The hand
        // ================================================================================================================

        private static List<Core2HandCardModel> Hand(Core2Fight fight, Core2CombatDisplay display, Core2CombatViewInputs i)
        {
            var cards = new List<Core2HandCardModel>();
            var live = !i.Busy && fight.Result == null;
            var hand = live ? fight.Hand : display.Hand;
            for (var index = 0; index < hand.Count; index++)
            {
                var instance = hand[index];
                var card = fight.Content.Card(instance.CardId);
                var carrier = fight.CardCarrier(instance);
                var maneuver = carrier.Profile?.Maneuver;
                var inHand = fight.Hand.Any(c => c.InstanceId == instance.InstanceId);
                Core2Costs costs = inHand ? fight.CardCosts(instance.InstanceId) : new Core2Costs(card.StaminaCost, card.ManaCost);
                var check = live && inHand ? fight.CheckPlay(instance.InstanceId, fight.CardTargetsEnemy(instance.InstanceId) ? fight.Enemies.FirstOrDefault(e => e.Alive)?.Id : null) : null;
                var sigil = SigilFor(card, carrier);
                string art = null;
                i.CardArt?.TryGetValue(instance.CardId, out art);
                cards.Add(new Core2HandCardModel
                {
                    InstanceId = instance.InstanceId, CardId = instance.CardId, Name = card.Name, Key = index < 9 ? Core2CombatText.Number(index + 1) : index == 9 ? "Q" : "",
                    Sp = costs.Sp, Mp = costs.Mp, ShowMp = costs.Mp > 0 || card.ManaCost > 0,
                    Playable = live && inHand && check == null, Refusal = live ? check?.Reason : null, Selected = i.Selected == instance.InstanceId,
                    Maneuver = maneuver, TypeLabel = Sigils[sigil].Label.ToUpperInvariant(), Sigil = Sigils[sigil].Mark,
                    FaceLines = inHand ? FaceLines(fight, instance) : new List<string>(), Art = art, Range = RangeLabel(carrier.Profile?.Range),
                    TargetsEnemy = inHand && fight.CardTargetsEnemy(instance.InstanceId), Reaction = card.Tags.Contains("trait:reaction"),
                });
            }
            return cards;
        }

        private static string SigilFor(Core2Card card, Core2Carrier carrier)
        {
            var maneuver = carrier.Profile?.Maneuver;
            if (maneuver != null && Sigils.ContainsKey(maneuver) && maneuver != "spell") return maneuver;
            if (carrier.Profile?.Camp == "spell") return "spell";
            var type = card.Type ?? "skill";
            return type == "attack" ? "attack" : type == "power" ? "power" : type == "status" || type == "curse" ? "status" : "skill";
        }

        private static string RangeLabel(string range) => range == "reach" ? "Reach" : range == "ranged" ? "Ranged" : range == "melee" || range == null ? "Melee" : Core2CombatText.Capitalize(range);

        /// <summary>
        /// The hand face's rule lines (JS cardFaceLines, compact): "12 damage" (×hits; "to all enemies" for an area card),
        /// a Counter's "Counter 9", "Gain 14 Block", a status "Weak 2", draw and heal; the authored template otherwise.
        /// Numbers are the live preview the domain gives (Core2Fight.CardFaceValues).
        /// </summary>
        public static List<string> FaceLines(Core2Fight fight, Core2CardInstance instance)
        {
            var card = fight.Content.Card(instance.CardId);
            var values = fight.CardFaceValues(instance);
            var counter = fight.CardCarrier(instance).Profile?.Maneuver == "counter";
            var lines = new List<string>();
            foreach (var effect in card.Effects.OfType<JObject>())
            {
                var op = (string)effect["op"];
                var target = (string)effect["target"];
                var area = target == "allEnemies";
                var amount = effect["amount"];
                var whole = amount != null && (amount.Type == JTokenType.Integer || amount.Type == JTokenType.Float) ? (int?)Math.Floor((double)amount) : null;
                switch (op)
                {
                    case "damage":
                        if (values.Damage == null) break;
                        lines.Add(counter ? "Counter " + values.Damage : (values.Hits > 1 ? values.Damage + "×" + values.Hits : Core2CombatText.Number(values.Damage.Value)) + " damage" + (area ? " to all enemies" : ""));
                        values.Damage = null;
                        break;
                    case "block":
                        if (values.Block == null) break;
                        lines.Add("Gain " + values.Block + " Block");
                        values.Block = null;
                        break;
                    case "applyStatus":
                    {
                        var status = (string)effect["status"];
                        var row = fight.Content.Statuses.FirstOrDefault(s => s.Id == status);
                        var stacks = effect["stacks"] != null && effect["stacks"].Type == JTokenType.Integer ? (int)effect["stacks"] : 1;
                        lines.Add(((string)row?.Raw["name"] ?? Core2CombatText.Words(status)) + " " + stacks + (area ? " to all enemies" : ""));
                        break;
                    }
                    case "draw": if (whole != null) lines.Add("Draw " + whole); break;
                    case "heal": if (whole != null) lines.Add("Heal " + whole); break;
                    case "gainEnergy": case "gainStamina": if (whole != null) lines.Add("Gain " + whole + " SP"); break;
                    case "restoreMana": if (whole != null) lines.Add("Gain " + whole + " MP"); break;
                }
            }
            if (lines.Count == 0 && !string.IsNullOrWhiteSpace(card.TextTemplate))
                lines.Add(System.Text.RegularExpressions.Regex.Replace(card.TextTemplate.Trim(), "\\{[^}]+\\}", "X"));
            return lines;
        }

        // ================================================================================================================
        // The footer
        // ================================================================================================================

        private static Core2CombatFooterModel Footer(Core2CombatViewInputs i, Core2CombatDisplay display, List<Core2HandCardModel> hand)
        {
            var fight = i.Fight;
            var playable = hand.Any(c => c.Playable);
            var canAct = !i.Busy && fight.Result == null && fight.PendingReaction == null && !fight.HasCardChoice && i.ManeuverPick == null && fight.Phase == "player";
            var footer = new Core2CombatFooterModel
            {
                Sp = display.Sp, MaxSp = display.MaxSp, Mp = display.Mp, MaxMp = display.MaxMp, Draw = display.Draw, Discard = display.Discard, Exhaust = display.Exhaust,
                DrawLabel = Core2CombatText.T("js.actionRow.draw"),
                DiscardLabel = Core2CombatText.T("combat.discard") + " " + display.Discard, ExhaustLabel = Core2CombatText.T("combat.exhaust") + " " + display.Exhaust,
                EndTurnLabel = Core2CombatText.T("combat.endTurn"), EndTurnKey = Core2CombatText.T("js.actionRow.endTurnKey"),
                EndTurnNeedsHold = playable, EndTurnHint = Core2CombatText.T("js.actionRow.hold"), EndTurnEnabled = canAct || i.CardChoiceDeferred,
                ReactionsEnabled = fight.ReactionsEnabled, Auto = i.Auto, ReactionSwitchEnabled = canAct,
            };
            footer.ReactionState = Core2CombatText.T(fight.ReactionsEnabled ? "combat.reactions.enabled" : "combat.reactions.disabled");
            footer.AutoState = Core2CombatText.T(i.Auto ? "combat.reactions.enabled" : "combat.reactions.disabled");
            footer.Flasks = Flasks(i, canAct);
            return footer;
        }

        public static List<Core2FlaskModel> Flasks(Core2CombatViewInputs i, bool canAct)
        {
            var flasks = new List<Core2FlaskModel>();
            var session = i.Session;
            if (session == null) return flasks;
            var run = session.Run;
            foreach (var kind in new[] { "hp", "mana" })
            {
                JObject def;
                try { def = Core2RunFlasks.ChargeFlask(session.Data, kind); } catch (InvalidOperationException) { continue; }
                var charges = kind == "hp" ? run.FlaskCharges?.HpCurrent ?? 0 : run.FlaskCharges?.ManaCurrent ?? 0;
                flasks.Add(new Core2FlaskModel
                {
                    ChargeKind = kind, Name = (string)def["name"], Text = (string)def["textTemplate"], Charges = charges, Usable = canAct && charges > 0,
                    Art = "Art/js1815/ui/icon-flask-" + (kind == "hp" ? "health" : "mana"),
                });
            }
            for (var slot = 0; slot < run.Flasks.Count; slot++)
            {
                var def = session.Data.Flask(run.Flasks[slot]);
                var hostile = (def["effects"] as JArray ?? new JArray()).OfType<JObject>().Any(e => (string)e["target"] == "enemy");
                flasks.Add(new Core2FlaskModel
                {
                    Slot = slot, Name = (string)def["name"], Text = (string)def["textTemplate"], Charges = 1, Usable = canAct, NeedsTarget = hostile,
                    Art = "Art/js1815/ui/icon-flask-" + ((string)def["icon"] == "flask-mana" ? "mana" : "health"),
                });
            }
            return flasks;
        }

        // ================================================================================================================
        // The reaction window
        // ================================================================================================================

        /// <summary>JS reactionStackEntryName: a player entry's card, or an enemy entry as "Gilded Knight: Gilded Guard".</summary>
        public static string StackEntryName(Core2Fight fight, Core2ReactionEntry entry)
        {
            if (entry.Side == "enemy")
            {
                var def = fight.Content.Enemy(entry.EnemyId ?? fight.Find(entry.OwnerId)?.EnemyId);
                var move = (string)def.Move(entry.MoveId)?.Raw["name"] ?? Core2CombatText.Words(entry.MoveId);
                return string.IsNullOrEmpty(def.Name) ? move : def.Name + ": " + move;
            }
            return fight.Content.HasCard(entry.CardId) ? fight.Content.Card(entry.CardId).Name : entry.CardId ?? "";
        }

        public static Core2ReactionModalModel Reaction(Core2Fight fight, string selected)
        {
            var offer = fight.PendingReaction;
            if (offer == null) return null;
            var stack = fight.ReactionStack != null && fight.ReactionStack.GroupKey == offer.GroupKey
                ? Enumerable.Reverse(fight.ReactionStack.Entries).Select(e => StackEntryName(fight, e)).ToList() : new List<string>();
            var stacked = stack.Count > 0;
            var source = fight.Find(offer.SourceId);
            var model = new Core2ReactionModalModel
            {
                OfferId = offer.Id, SourceId = offer.SourceId, Eyebrow = Core2CombatText.T("combat.reaction.eyebrow"), Title = Core2CombatText.T("combat.reaction.title"),
                Help = Core2CombatText.T(stacked ? "combat.reaction.helpStack" : "combat.reaction.help"), Stack = stack,
                StackLabel = stacked ? Core2CombatText.T("combat.reaction.stack") : null,
                DeclineLabel = Core2CombatText.T(stacked ? "combat.reaction.resolve" : "common.back"), PlayLabel = Core2CombatText.T("combat.reaction.play"),
                Selected = offer.Options.Any(o => o.Id == selected) ? selected : null,
            };
            model.PlayEnabled = model.Selected != null;
            // The threat: a stacked enemy reaction of the source answers the player's own card; else the source's read move.
            var enemyEntry = fight.ReactionStack?.GroupKey == offer.GroupKey
                ? Enumerable.Reverse(fight.ReactionStack.Entries).FirstOrDefault(e => e.Side == "enemy" && e.OwnerId == offer.SourceId) : null;
            if (source != null && source.Side == Core2Side.Enemy)
            {
                if (enemyEntry != null) model.Threat = StackEntryName(fight, enemyEntry);
                else
                {
                    var view = Core2Knowledge.Project(source, fight.Turn);
                    var move = view.Hidden || view.MoveId == null ? null : (string)fight.EnemyDef(source).Move(view.MoveId)?.Raw["name"] ?? Core2CombatText.Words(view.MoveId);
                    model.Threat = move == null ? fight.EnemyDef(source).Name : fight.EnemyDef(source).Name + ": " + move;
                }
                if (enemyEntry == null)
                {
                    var contact = fight.Events.LastOrDefault(e => e.Type == "combatContactPrepared" && (string)e.Data["contactKey"] == offer.GroupKey);
                    var moveId = (string)contact?.Data["moveId"];
                    // JS captureEnemyIntent: only an acting enemy's revealed move reads (a Counter return out of turn does not).
                    if (contact != null && source.ActorIntentRevealed && (moveId == null || moveId == source.Intent?.MoveId))
                    {
                        var read = DamageRead(fight, source, ((int?)contact.Data["contactIndex"] ?? 0, (int?)contact.Data["contactCount"] ?? 1));
                        if (read != null) model.Incoming = read.Label + " · " + read.Summary;
                    }
                }
            }
            foreach (var option in offer.Options)
            {
                var instance = fight.Hand.FirstOrDefault(c => c.InstanceId == option.Play.CardInstanceId);
                var row = new Core2ReactionOptionModel { Id = option.Id, Name = option.Name, CardId = option.CardId, InstanceId = option.Play.CardInstanceId };
                if (instance != null) row.Summary = string.Join(" · ", FaceLines(fight, instance));
                if (option.StaminaCost > 0 || option.ManaCost == 0) row.Costs.Add(Core2CombatText.T("combat.reaction.sp", ("amount", option.StaminaCost)));
                if (option.ManaCost > 0) row.Costs.Add(Core2CombatText.T("combat.reaction.mp", ("amount", option.ManaCost)));
                model.Options.Add(row);
            }
            return model;
        }

        // ================================================================================================================
        // The enemy inspector (JS combatantDetailBody over Core2EnemyInspector's data)
        // ================================================================================================================

        public static Core2InspectorModel Inspect(Core2Fight fight, Core2Combatant enemy)
        {
            var data = Core2EnemyInspector.Inspect(fight, enemy);
            var def = fight.EnemyDef(enemy);
            var model = new Core2InspectorModel
            {
                EnemyId = enemy.Id, Name = data.Name, Figure = EnemyArt + enemy.EnemyId + "-idle",
                Subtitle = data.Traits.Count > 0 ? string.Join(" · ", data.Traits.Select(Core2CombatText.Words)) : Core2CombatText.T("combat.log.role.enemy"),
            };
            foreach (var r in data.Resources) model.Meters.Add((r.Label, r.Max == null ? Core2CombatText.Number(r.Value) : r.Value + " / " + r.Max));
            var hp = data.Resources.First(r => r.Label == "HP");
            var block = data.Resources.FirstOrDefault(r => r.Label == "Block");
            string intentName, intentDetail;
            if (data.Intent.Hidden) { intentName = Core2CombatText.T("js.inspector.hiddenIntent", ("stance", "unknown")); intentDetail = Core2CombatText.T("js.inspector.hiddenDetail"); }
            else
            {
                var badge = Intent(fight, enemy);
                intentName = badge.Title;
                intentDetail = string.Join(" · ", badge.Parts.Where(p => p.Value != null).Select(p => p.Value + (p.Summary != null ? " (" + p.Summary + ")" : "")));
            }
            var summary = new Core2InspectorSection { Id = "summary", Title = Core2CombatText.T("inspector.section.summary") };
            summary.Rows.Add((Core2CombatText.T("inspector.summary.hp"), hp.Value + " / " + hp.Max));
            foreach (var sp in data.Resources.Where(r => r.Label == "SP")) summary.Rows.Add(("SP", sp.Value + " / " + sp.Max));
            summary.Rows.Add((Core2CombatText.T("inspector.summary.intent"), intentName + (string.IsNullOrEmpty(intentDetail) ? "" : " · " + intentDetail)));
            summary.Rows.Add((Core2CombatText.T("inspector.summary.defense"), Core2CombatText.T("inspector.summary.block", ("amount", block?.Value ?? 0))));
            model.Sections.Add(summary);
            var state = new Core2InspectorSection { Id = "state", Title = Core2CombatText.T("inspector.section.state") };
            foreach (var r in data.Resources.Where(r => r.Label != "HP" && r.Label != "Block" && r.Label != "SP" && (r.Max == null ? r.Value > 0 : r.Max > 0)))
                state.Rows.Add((r.Label, r.Max == null ? Core2CombatText.Number(r.Value) : r.Value + " / " + r.Max));
            if (data.Stance != null) state.Rows.Add(("Stance", Core2CombatText.Capitalize(data.Stance)));
            foreach (var s in data.Statuses)
            {
                var row = fight.Content.Statuses.FirstOrDefault(x => x.Id == s.Key);
                state.Rows.Add((((string)row?.Raw["name"] ?? Core2CombatText.Words(s.Key)) + " " + s.Value, (string)row?.Raw["tooltip"] ?? ""));
            }
            if (state.Rows.Count == 0) state.Knowledge = "none";
            model.Sections.Add(state);
            var history = new Core2InspectorSection { Id = "history", Title = Core2CombatText.T("inspector.section.history") };
            foreach (var moveId in Enumerable.Reverse(data.History)) history.Rows.Add((Core2CombatText.Words(moveId), ""));
            if (history.Rows.Count == 0) history.Knowledge = "none";
            model.Sections.Add(history);
            if (data.Deck != null)
            {
                var deck = new Core2InspectorSection { Id = "enemy-deck", Title = Core2CombatText.T("js.inspector.enemyDeck") };
                deck.Rows.Add((Core2CombatText.T("js.inspector.enemyDeckNote"), ""));
                deck.Rows.Add((Core2CombatText.T("js.inspector.currentAction", ("name", data.Current?.Card.Name ?? (data.CurrentHidden ? Core2CombatText.T("gameProfile.combat.unreadIntent") : Core2CombatText.T("js.inspector.none")))), ""));
                deck.Rows.Add((Core2CombatText.T("js.inspector.lastPlayed", ("name", data.Last?.Card.Name ?? Core2CombatText.T("js.inspector.noneYet"))), data.Last?.PaidSp != null ? data.Last.PaidSp + " SP" : ""));
                deck.Rows.Add((Core2CombatText.T("js.inspector.authoredDeck", ("count", data.Deck.Sum(c => Math.Max(1, c.Copies)))), ""));
                foreach (var card in data.Deck)
                    deck.Rows.Add((card.Name + (card.Copies > 1 ? " ×" + card.Copies : ""), card.ActionSp + " SP" + (card.Locked ? " · Locked" : "") + (card.Active ? " · Current" : "")));
                model.Sections.Add(deck);
            }
            var moves = new Core2InspectorSection { Id = "abilities", Title = Core2CombatText.T("js.combat.moveSet") };
            foreach (var move in data.Moves)
            {
                var detail = new List<string> { move.Sp + " SP" };
                if (move.BaseDamage != null) detail.Add(move.Hits > 1 ? move.BaseDamage + "×" + move.Hits + " damage" : move.BaseDamage + " damage");
                if (move.BaseBlock != null) detail.Add(move.BaseBlock + " Block");
                if (move.Locked) detail.Add(move.UnlockedBy.Count > 0 ? "Locked until " + string.Join(", ", move.UnlockedBy.Select(u => u.StartsWith("hpBelowPct:", StringComparison.Ordinal) ? "HP below " + u.Substring(11) + "%" : Core2CombatText.Words(u))) : "Locked");
                moves.Rows.Add((move.Name + (move.Active ? " (current)" : ""), string.Join(" · ", detail)));
            }
            model.Sections.Add(moves);
            var traits = new Core2InspectorSection { Id = "traits", Title = Core2CombatText.T("inspector.section.traits") };
            foreach (var tag in data.Traits) traits.Rows.Add((Core2CombatText.Words(tag), ""));
            if (traits.Rows.Count == 0) traits.Knowledge = "none";
            model.Sections.Add(traits);
            return model;
        }
    }
}
