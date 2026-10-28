// Core2RunDeck.cs — a Core 2 run's collection and deck editing (port plan phase 9), engine-free.
//
// JS at the pin:
//   src/model/collectedCards.js   hasCollectedCardPolicy (Core 2 runs), looseCollectedCard, knownCollectedCardIds (the
//                                 ledger plus every loose owned definition), collectedCardProblems, recordCollectedCards
//   src/model/deckCopyLimit.js    activeAdeptTraining (training.basic: Martial Adept, Basic; training.graduate: Magic
//                                 Adept, Cantrip), collectedAdeptCardIds (the class's compiled catalogue, exported as
//                                 data, ∩ the collected definitions), configuredDeckCopyLimit (an Adept card is
//                                 unlimited; any other card is limited to the copies owned, deck ∪ sideboard)
//   src/model/deckRules.js        deckEditBounds / deckEditRefusal (Settings: minimum 10, maximum unlimited by default),
//                                 basicCardIds / unlimitedCardIds (Core 2: the Adept cards), isUnlimitedBasic,
//                                 isSetAsideBasic, deckCardEquipmentEligible, moveToSideboard (a pristine Adept basic is
//                                 deleted, every other card kept), moveFromSideboard (equipment gate, copy limit),
//                                 addBasicCard (plain: a kept copy back first, else a new `edit:N`), beginDeckEdit /
//                                 cancelDeckEdit (deck, sideboard, mint counter and collection knowledge)
//   src/ui/models/DeckEditorModel.js openDeckEdit (add by tile key, remove, move under Play in deck order, Undo, Confirm
//                                 inside the bounds, Cancel restores everything), deckVariantKey, the collection tiles
//   src/model/gameProfileRewards.js rewardDeckAddRefusal / claimGameProfileRewardToDeck (a won card straight into the deck)
//   src/model/armouryRules.js     editingLocked (no edit while a fight is unresolved), armouryOpensDeckEditor
//
// A Core 2 deck has no attack-slot basics (the cards module is configured), so the slot allocation the legacy editor
// also restores is absent; range mechanics are on for every Core 2 run (run.rangePolicy.enabled), so no movement-only
// card is withheld.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>The player's Deck settings (Settings → Advanced → Deck); defaults are content/deckRules.js.</summary>
    public sealed class Core2DeckSettings
    {
        public bool DeckEditing { get; set; } = true;
        public string DeckEditingWhere { get; set; } = "free";
        public JToken DeckMinSize { get; set; } = 10;
        public bool DeckMinUnlimited { get; set; }
        public JToken DeckMaxSize { get; set; } = 40;
        public bool DeckMaxUnlimited { get; set; } = true;
        public bool PlayInDeckOrder { get; set; }

        /// <summary>The defaults, with any stored choice laid over them (an unset key reads its default).</summary>
        public static Core2DeckSettings From(Core2RunData data, JObject stored = null)
        {
            var d = (JObject)data.DeckRules["defaults"];
            JToken Get(string key) => stored?[key] != null && stored[key].Type != JTokenType.Null ? stored[key] : d[key];
            var where = (string)Get("deckEditingWhere");
            return new Core2DeckSettings
            {
                DeckEditing = (bool)Get("deckEditing"),
                DeckEditingWhere = Core2Json.Strings(data.DeckRules["where"]).Contains(where) ? where : (string)d["deckEditingWhere"],
                DeckMinSize = Get("deckMinSize"), DeckMinUnlimited = (bool)Get("deckMinUnlimited"),
                DeckMaxSize = Get("deckMaxSize"), DeckMaxUnlimited = (bool)Get("deckMaxUnlimited"),
                PlayInDeckOrder = (bool)Get("playInDeckOrder"),
            };
        }
    }

    /// <summary>One collection tile of the deck editor (JS deckEditorModel collection row, without its presentation text).</summary>
    public sealed class Core2DeckTile
    {
        public string Key { get; set; }
        public string CardId { get; set; }
        public bool Unlimited { get; set; }
        /// <summary>Copies owned; null for an unlimited (minted) basic.</summary>
        public int? Owned { get; set; }
        public int InDeck { get; set; }
        public bool Addable { get; set; }
        /// <summary>Why the tile cannot add: equipment, range, noOwned, allInDeck, copyLimit; null when addable.</summary>
        public string Refusal { get; set; }
        public string RemoveInstanceId { get; set; }
        public JObject ToJson() => new JObject
        {
            ["key"] = Key, ["cardId"] = CardId, ["unlimited"] = Unlimited, ["owned"] = Owned.HasValue ? (JToken)Owned.Value : JValue.CreateNull(), ["inDeck"] = InDeck,
            ["addable"] = Addable, ["refusal"] = Refusal, ["removeInstanceId"] = RemoveInstanceId,
        };
    }

    /// <summary>A deck change refused by the rules, with its code (JS reward add: notCard, full, limit).</summary>
    public sealed class Core2DeckRefused : InvalidOperationException
    {
        public string Code { get; }
        public Core2DeckRefused(string code, string message) : base(message) { Code = code; }
    }

    public static class Core2RunDeck
    {
        // ---- collection -------------------------------------------------------------------------------------------

        public static bool LooseCollectedCard(Core2RunCard card) => card != null && card.GrantedBy == null && card.EquipmentRole == null;

        /// <summary>JS knownCollectedCardIds: the ledger, then every loose owned definition, authored definitions only, once each.</summary>
        public static List<string> KnownCollectedCardIds(Core2RunData data, Core2Run run)
        {
            var definitions = new HashSet<string>(data.Content.Cards.Select(c => c.Id), StringComparer.Ordinal);
            var next = new List<string>();
            foreach (var id in (run.CollectedCardIds ?? new List<string>()).Concat(run.Owned.Where(LooseCollectedCard).Select(c => c.CardId)))
                if (definitions.Contains(id) && !next.Contains(id)) next.Add(id);
            return next;
        }

        /// <summary>JS collectedCardProblems: the ledger must hold unique authored definition ids.</summary>
        public static List<string> CollectedCardProblems(Core2RunData data, Core2Run run)
        {
            var ids = run.CollectedCardIds;
            if (ids == null) return new List<string>();
            var definitions = new HashSet<string>(data.Content.Cards.Select(c => c.Id), StringComparer.Ordinal);
            if (ids.Any(id => id == null || !definitions.Contains(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                return new List<string> { "collectedCardIds must contain unique authored card definition IDs" };
            return new List<string>();
        }

        /// <summary>JS recordCollectedCards: refuse a malformed ledger, then record what the run now knows; true when it changed.</summary>
        public static bool RecordCollectedCards(Core2RunData data, Core2Run run)
        {
            var problems = CollectedCardProblems(data, run);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join("; ", problems));
            var next = KnownCollectedCardIds(data, run);
            if (run.CollectedCardIds != null && run.CollectedCardIds.SequenceEqual(next)) return false;
            run.CollectedCardIds = next;
            return true;
        }

        // ---- Adept entitlements -----------------------------------------------------------------------------------

        /// <summary>JS activeAdeptTraining: { id, label, kind } for the run's class, or null.</summary>
        public static JObject AdeptTraining(Core2RunData data, Core2Run run) => data.AdeptTraining(run.Class);

        /// <summary>JS collectedAdeptCardIds: the class's training catalogue, kept to the definitions this run has collected.</summary>
        public static List<string> AdeptCardIds(Core2RunData data, Core2Run run)
        {
            if (AdeptTraining(data, run) == null) return new List<string>();
            var known = new HashSet<string>(KnownCollectedCardIds(data, run), StringComparer.Ordinal);
            return data.AdeptCatalogue(run.Class).Where(known.Contains).ToList();
        }

        /// <summary>JS unlimitedCardIds (Core 2): the Adept cards this run may mint without limit.</summary>
        public static List<string> UnlimitedCardIds(Core2RunData data, Core2Run run) => AdeptCardIds(data, run);

        /// <summary>JS isUnlimitedBasic (configured cards): a loose card whose id is an Adept card.</summary>
        public static bool IsUnlimitedBasic(Core2RunData data, Core2Run run, Core2RunCard card) =>
            card != null && !card.ItemOwned && UnlimitedCardIds(data, run).Contains(card.CardId);

        /// <summary>JS isSetAsideBasic: an unlimited basic worth keeping apart from a fresh one (an upgraded copy).</summary>
        public static bool IsSetAsideBasic(Core2RunData data, Core2Run run, Core2RunCard card) =>
            card != null && card.EquipmentRole == null && IsUnlimitedBasic(data, run, card) && card.Upgraded;

        /// <summary>JS ownedCopies: deck ∪ sideboard copies of a card id.</summary>
        public static int OwnedCopies(Core2Run run, string cardId) => run.Owned.Count(c => c.CardId == cardId);

        /// <summary>JS configuredDeckCopyLimit (Core 2): null (no limit) for an Adept card, else the copies owned.</summary>
        public static int? CopyLimit(Core2RunData data, Core2Run run, string cardId) => AdeptCardIds(data, run).Contains(cardId) ? (int?)null : OwnedCopies(run, cardId);

        /// <summary>JS deckCardEquipmentEligible: a lent card needs its mount; a school card needs a held piece of that school.</summary>
        public static bool EquipmentEligible(Core2RunData data, Core2Run run, Core2RunCard card)
        {
            if (card == null) return false;
            if (card.ItemOwned) return Core2RunGear.GrantInstances(data, run).Any(d => d.InstanceId == card.InstanceId && d.CardId == card.CardId);
            var tags = data.Content.Card(card.CardId).Tags;
            var rules = (JObject)data.DeckRules["equipmentEligibility"];
            var universal = Core2Json.Strings(rules["universalTags"]);
            if (tags.Any(universal.Contains)) return true;
            var schools = new HashSet<string>(data.EquipmentSchools, StringComparer.Ordinal);
            var required = tags.Where(schools.Contains).ToList();
            if (required.Count == 0) return true;
            var held = new HashSet<string>(Core2RunGear.Worn(data, run.Loadout, run.Class).SelectMany(p => Core2Json.Strings(p.Row["tags"])), StringComparer.Ordinal);
            var weaponTags = Core2Json.Strings(rules["weaponTags"]);
            var identities = required.Where(weaponTags.Contains).ToList();
            return identities.Count > 0 ? identities.Any(held.Contains) : required.Any(held.Contains);
        }

        // ---- one-step edits ---------------------------------------------------------------------------------------

        /// <summary>JS moveToSideboard: every card may leave; a pristine Adept basic is deleted, anything else kept.</summary>
        public static bool MoveToSideboard(Core2RunData data, Core2Run run, string instanceId)
        {
            var index = run.Deck.FindIndex(c => c.InstanceId == instanceId);
            if (index < 0) return false;
            var card = run.Deck[index];
            RecordCollectedCards(data, run);
            run.Deck.RemoveAt(index);
            if (card.EquipmentRole == null && card.GrantedBy == null && UnlimitedCardIds(data, run).Contains(card.CardId) && !card.Upgraded) return true;
            run.Sideboard.Add(card);
            return true;
        }

        /// <summary>JS moveFromSideboard: the equipment gate, then the copy limit; a lent card re-stamps the deck.</summary>
        public static bool MoveFromSideboard(Core2RunData data, Core2Run run, string instanceId)
        {
            var index = run.Sideboard.FindIndex(c => c.InstanceId == instanceId);
            if (index < 0) return false;
            var card = run.Sideboard[index];
            if (!EquipmentEligible(data, run, card)) return false;
            var inDeck = run.Deck.Count(c => c.CardId == card.CardId);
            var limit = CopyLimit(data, run, card.CardId);
            if (limit.HasValue && inDeck >= limit.Value) return false;
            run.Sideboard.RemoveAt(index);
            run.Deck.Add(card);
            if (card.EquipmentRole != null) { Core2RunGear.ReconcilePools(data, run); Core2RunGear.ReconcileGrantedCards(data, run); }
            return true;
        }

        private static string MintId(Core2Run run)
        {
            var held = new HashSet<string>(run.Owned.Select(c => c.InstanceId), StringComparer.Ordinal);
            do run.EditMintCounter = (run.EditMintCounter ?? 0) + 1;
            while (held.Contains("edit:" + run.EditMintCounter.Value));
            return "edit:" + run.EditMintCounter.Value;
        }

        /// <summary>JS addBasicCard (plain): a kept copy of the Adept card back first, else a new `edit:N` copy.</summary>
        public static Core2RunCard AddBasic(Core2RunData data, Core2Run run, string cardId, bool freshOnly = false)
        {
            var unlimited = UnlimitedCardIds(data, run);
            if (!unlimited.Contains(cardId)) throw new Core2DeckRefused("notUnlimited", "'" + cardId + "' is not an unlimited card id (" + string.Join(", ", unlimited) + ")");
            if (!EquipmentEligible(data, run, new Core2RunCard { CardId = cardId })) throw new Core2DeckRefused("equipment", "This card requires compatible equipment and range mechanics");
            var kept = run.Sideboard.FirstOrDefault(c => c.EquipmentRole == null && c.GrantedBy == null && c.CardId == cardId && (!freshOnly || !IsSetAsideBasic(data, run, c)));
            if (kept != null) { MoveFromSideboard(data, run, kept.InstanceId); return kept; }
            var card = new Core2RunCard { InstanceId = MintId(run), CardId = cardId };
            run.Deck.Add(card);
            return card;
        }

        // ---- bounds -----------------------------------------------------------------------------------------------

        private static int Whole(JToken value, string key, int min)
        {
            if (value == null || value.Type != JTokenType.Integer || (long)value < min) throw new InvalidOperationException(key + " must be a whole number of at least " + min + " (got " + (value?.ToString(Newtonsoft.Json.Formatting.None) ?? "undefined") + ")");
            return (int)value;
        }

        /// <summary>JS deckEditBounds: { min, max (null = unlimited), problem }; a maximum below the minimum is refused by name.</summary>
        public static (int Min, int? Max, string Problem) Bounds(Core2DeckSettings settings)
        {
            int min; int? max;
            try
            {
                min = settings.DeckMinUnlimited ? 0 : Whole(settings.DeckMinSize, "deckMinSize", 0);
                max = settings.DeckMaxUnlimited ? (int?)null : Whole(settings.DeckMaxSize, "deckMaxSize", 1);
            }
            catch (InvalidOperationException error)
            {
                int fallback = 10;
                try { fallback = settings.DeckMinUnlimited ? 0 : Whole(settings.DeckMinSize, "deckMinSize", 0); } catch (InvalidOperationException) { }
                return (fallback, fallback, error.Message);
            }
            if (max.HasValue && max.Value < min)
                return (min, min, "Maximum deck size (" + max.Value + ") is below the minimum (" + min + "); the editor cannot confirm any deck until one of them moves.");
            return (min, max, "");
        }

        /// <summary>JS deckEditRefusal: '' when a deck of <paramref name="count"/> cards may be confirmed, else one sentence.</summary>
        public static string EditRefusal(int count, Core2DeckSettings settings)
        {
            var (min, max, problem) = Bounds(settings);
            if (problem.Length > 0) return problem;
            string Cards(int n) => n + " card" + (n == 1 ? "" : "s");
            if (count < min) return "Your deck has " + Cards(count) + "; it needs at least " + min + ".";
            if (max.HasValue && count > max.Value) return "Your deck has " + Cards(count) + "; it can hold at most " + max.Value + ".";
            return "";
        }

        /// <summary>JS deckVariantKey for a Core 2 instance: card id, upgraded, mods (none), lending piece.</summary>
        public static string VariantKey(Core2RunCard card) => card.CardId + "~" + (card.Upgraded ? "u" : "") + "~" + (card.GrantedBy != null ? "~" + card.GrantedBy : "");

        // ---- the collection tiles ---------------------------------------------------------------------------------

        /// <summary>JS deckEditorModel collection: the Adept mints, kept (set-aside) basics, then every limited card owned or collected.</summary>
        public static List<Core2DeckTile> Tiles(Core2RunData data, Core2Run run)
        {
            var tiles = new List<Core2DeckTile>();
            string LastIn(Func<Core2RunCard, bool> belongs) { for (var i = run.Deck.Count - 1; i >= 0; i--) if (belongs(run.Deck[i])) return run.Deck[i].InstanceId; return null; }
            bool Fresh(Core2RunCard c) => c.GrantedBy == null && !IsSetAsideBasic(data, run, c);
            var unlimited = UnlimitedCardIds(data, run);
            foreach (var cardId in unlimited)
            {
                bool Belongs(Core2RunCard c) => c.EquipmentRole == null && c.GrantedBy == null && c.CardId == cardId && Fresh(c);
                var eligible = EquipmentEligible(data, run, new Core2RunCard { CardId = cardId });
                tiles.Add(new Core2DeckTile { Key = "basic:" + cardId, CardId = cardId, Unlimited = true, Owned = null, InDeck = run.Deck.Count(Belongs),
                    Addable = eligible, Refusal = eligible ? null : "equipment", RemoveInstanceId = LastIn(Belongs) });
            }
            var kept = new List<(string Variant, Core2RunCard Card)>();
            foreach (var card in run.Deck.Concat(run.Sideboard))
                if (IsSetAsideBasic(data, run, card) && kept.All(k => k.Variant != VariantKey(card))) kept.Add((VariantKey(card), card));
            foreach (var (variant, card) in kept)
            {
                bool Belongs(Core2RunCard c) => IsSetAsideBasic(data, run, c) && VariantKey(c) == variant;
                var count = run.Sideboard.Count(Belongs);
                tiles.Add(new Core2DeckTile { Key = "kept:" + variant, CardId = card.CardId, Unlimited = false, Owned = count, InDeck = run.Deck.Count(Belongs),
                    // JS: nothing to restore with no sideboard copy; the tile is shown, not addable, and gives no refusal.
                    Addable = count > 0, Refusal = null, RemoveInstanceId = LastIn(Belongs) });
            }
            var limited = new List<(string Variant, Core2RunCard Card, int Owned, int InDeck, int Loose)>();
            foreach (var card in run.Deck.Concat(run.Sideboard))
            {
                if (IsUnlimitedBasic(data, run, card)) continue;
                var variant = VariantKey(card);
                var i = limited.FindIndex(e => e.Variant == variant);
                var inDeck = run.Deck.Contains(card);
                if (i < 0) limited.Add((variant, card, 1, inDeck ? 1 : 0, inDeck ? 0 : 1));
                else limited[i] = (variant, limited[i].Card, limited[i].Owned + 1, limited[i].InDeck + (inDeck ? 1 : 0), limited[i].Loose + (inDeck ? 0 : 1));
            }
            foreach (var cardId in KnownCollectedCardIds(data, run))
            {
                if (unlimited.Contains(cardId) || limited.Any(e => e.Card.CardId == cardId)) continue;
                var card = new Core2RunCard { CardId = cardId };
                limited.Add((VariantKey(card), card, 0, 0, 0));
            }
            foreach (var entry in limited)
            {
                var limit = CopyLimit(data, run, entry.Card.CardId);
                var inDeckById = run.Deck.Count(c => c.CardId == entry.Card.CardId);
                string refusal = null;
                if (!EquipmentEligible(data, run, entry.Card)) refusal = "equipment";
                else if (entry.Owned == 0) refusal = "noOwned";
                else if (entry.Loose == 0) refusal = "allInDeck";
                else if (limit.HasValue && inDeckById >= limit.Value) refusal = "copyLimit";
                tiles.Add(new Core2DeckTile { Key = "card:" + entry.Variant, CardId = entry.Card.CardId, Unlimited = false, Owned = entry.Owned, InDeck = entry.InDeck,
                    Addable = refusal == null, Refusal = refusal, RemoveInstanceId = LastIn(c => VariantKey(c) == entry.Variant) });
            }
            return tiles;
        }

        // ---- a won card straight into the deck --------------------------------------------------------------------

        private static Core2RewardOption AddRewardToDeck(Core2RunData data, Core2Run draft, string offerId, int index, Core2DeckSettings settings)
        {
            var offer = draft.RewardOffers.TryGetValue(offerId ?? "", out var o) ? o : null;
            var option = offer != null && index >= 0 && index < offer.Options.Count ? offer.Options[index] : null;
            if (option == null || (option.Kind != "combatCards" && option.Kind != "weaponArts")) throw new Core2DeckRefused("notCard", "Only a card can be added to the deck");
            var instanceId = "reward:" + offerId + ":" + index;
            if (draft.Deck.Any(c => c.InstanceId == instanceId)) return option;
            if (!offer.Taken.Contains(index)) Core2RunRewards.Claim(data, draft, offerId, index);
            var max = Bounds(settings).Max;
            if (max.HasValue && draft.Deck.Count + 1 > max.Value) throw new Core2DeckRefused("full", "Your deck is full (" + max.Value + " cards)");
            if (!MoveFromSideboard(data, draft, instanceId)) throw new Core2DeckRefused("limit", "This card cannot join the deck right now");
            RecordCollectedCards(data, draft);
            return option;
        }

        /// <summary>JS rewardDeckAddRefusal: '' when "Add to deck" is legal for the offered card, else notCard, full or limit.</summary>
        public static string RewardDeckAddRefusal(Core2RunData data, Core2Run run, string offerId, int index, Core2DeckSettings settings)
        {
            try { AddRewardToDeck(data, Core2RunSave.Clone(run), offerId, index, settings); return ""; }
            catch (Core2DeckRefused refused) { return refused.Code; }
        }

        /// <summary>JS claimGameProfileRewardToDeck: claim (when not yet claimed) and add the card to the deck, all or nothing.</summary>
        public static Core2RewardOption ClaimRewardToDeck(Core2RunData data, Core2Run run, string offerId, int index, Core2DeckSettings settings)
        {
            var draft = Core2RunSave.Clone(run);
            var option = AddRewardToDeck(data, draft, offerId, index, settings);
            Core2RunSave.CopyInto(draft, run);
            return option;
        }
    }

    /// <summary>The deck-editor state an edit can change (JS EDIT_STATE_KEYS for a Core 2 run).</summary>
    internal sealed class Core2DeckSnapshot
    {
        private readonly List<Core2RunCard> _deck, _sideboard;
        private readonly int? _mint;
        private readonly List<string> _collected;
        public Core2DeckSnapshot(Core2Run run)
        {
            _deck = run.Deck.Select(c => c.Clone()).ToList(); _sideboard = run.Sideboard.Select(c => c.Clone()).ToList();
            _mint = run.EditMintCounter; _collected = run.CollectedCardIds == null ? null : new List<string>(run.CollectedCardIds);
        }
        public void Restore(Core2Run run)
        {
            run.Deck = _deck.Select(c => c.Clone()).ToList(); run.Sideboard = _sideboard.Select(c => c.Clone()).ToList();
            run.EditMintCounter = _mint; run.CollectedCardIds = _collected == null ? null : new List<string>(_collected);
        }
    }

    /// <summary>
    /// JS openDeckEdit: an editor session over the run, writing it in place; Cancel restores everything it can change and
    /// Confirm succeeds only inside the bounds. Add takes a collection tile key (basic:&lt;id&gt;, kept:&lt;variant&gt;,
    /// card:&lt;variant&gt; or card:&lt;cardId&gt;).
    /// </summary>
    public sealed class Core2DeckEdit
    {
        private readonly Core2RunData _data;
        private readonly Core2Run _run;
        private readonly Core2DeckSettings _settings;
        private readonly Core2DeckSnapshot _snapshot;
        private readonly List<Core2DeckSnapshot> _history = new List<Core2DeckSnapshot>();
        private readonly Action _onClose;
        public bool Closed { get; private set; }
        public bool CanUndo => !Closed && _history.Count > 0;

        internal Core2DeckEdit(Core2RunData data, Core2Run run, Core2DeckSettings settings, Action onClose = null)
        {
            _data = data; _run = run; _settings = settings ?? Core2DeckSettings.From(data); _onClose = onClose;
            _snapshot = new Core2DeckSnapshot(run);
        }

        /// <summary>Open a session on a run directly (a session-less tool or test); Core2RunSession.OpenDeckEdit adds the combat lock.</summary>
        public static Core2DeckEdit Open(Core2RunData data, Core2Run run, Core2DeckSettings settings = null) => new Core2DeckEdit(data, run, settings);

        private void Live() { if (Closed) throw new InvalidOperationException("openDeckEdit: this editor session is already closed"); }
        private (bool Ok, string Refusal) Remember(Core2DeckSnapshot before, (bool Ok, string Refusal) result) { if (result.Ok) _history.Add(before); return result; }

        public List<Core2DeckTile> Tiles() => Core2RunDeck.Tiles(_data, _run);
        public (int Count, int Min, int? Max, string Refusal) Counter()
        {
            var bounds = Core2RunDeck.Bounds(_settings);
            return (_run.Deck.Count, bounds.Min, bounds.Max, Core2RunDeck.EditRefusal(_run.Deck.Count, _settings));
        }

        /// <summary>JS add(key) → { ok, refusal }; a refused Adept mint throws as addBasicCard does.</summary>
        public (bool Ok, string Refusal) Add(string key)
        {
            Live();
            var before = new Core2DeckSnapshot(_run);
            var colon = (key ?? "").IndexOf(':');
            var kind = colon < 0 ? "" : key.Substring(0, colon);
            var id = colon < 0 ? "" : key.Substring(colon + 1);
            if (kind == "basic") return Remember(before, (Core2RunDeck.AddBasic(_data, _run, id, freshOnly: true) != null, ""));
            if (kind == "kept")
            {
                var card = _run.Sideboard.FirstOrDefault(c => Core2RunDeck.IsSetAsideBasic(_data, _run, c) && Core2RunDeck.VariantKey(c) == id);
                if (card == null) return (false, "");
                return Remember(before, (Core2RunDeck.MoveFromSideboard(_data, _run, card.InstanceId), ""));
            }
            if (kind != "card") throw new InvalidOperationException("openDeckEdit.add: unknown collection key '" + key + "'");
            Func<Core2RunCard, bool> matches = id.Contains('~') ? (Func<Core2RunCard, bool>)(c => Core2RunDeck.VariantKey(c) == id) : c => c.CardId == id;
            var loose = _run.Sideboard.FirstOrDefault(c => matches(c) && !Core2RunDeck.IsUnlimitedBasic(_data, _run, c));
            if (loose == null) return (false, "allInDeck");
            if (!Core2RunDeck.EquipmentEligible(_data, _run, loose)) return (false, "equipment");
            if (Core2RunDeck.MoveFromSideboard(_data, _run, loose.InstanceId)) return Remember(before, (true, ""));
            return (false, "copyLimit");
        }

        /// <summary>JS remove(instanceId) → { ok }.</summary>
        public (bool Ok, string Refusal) Remove(string instanceId)
        {
            Live();
            if (!_run.Deck.Any(c => c.InstanceId == instanceId)) return (false, "");
            var before = new Core2DeckSnapshot(_run);
            return Remember(before, (Core2RunDeck.MoveToSideboard(_data, _run, instanceId), ""));
        }

        /// <summary>JS moveTo: only under Play in deck order.</summary>
        public bool MoveTo(string instanceId, int index)
        {
            Live();
            if (!_settings.PlayInDeckOrder) return false;
            var from = _run.Deck.FindIndex(c => c.InstanceId == instanceId);
            var to = Math.Max(0, Math.Min(_run.Deck.Count - 1, index));
            if (from < 0 || from == to) return false;
            _history.Add(new Core2DeckSnapshot(_run));
            var card = _run.Deck[from];
            _run.Deck.RemoveAt(from);
            _run.Deck.Insert(to, card);
            return true;
        }

        public bool Move(string instanceId, int delta) => MoveTo(instanceId, _run.Deck.FindIndex(c => c.InstanceId == instanceId) + delta);

        public bool Undo()
        {
            Live();
            if (_history.Count == 0) return false;
            _history[_history.Count - 1].Restore(_run);
            _history.RemoveAt(_history.Count - 1);
            return true;
        }

        /// <summary>JS confirm(): refused outside the bounds (the sentence names them); the session closes on success.</summary>
        public (bool Ok, string Refusal) Confirm()
        {
            Live();
            var refusal = Core2RunDeck.EditRefusal(_run.Deck.Count, _settings);
            if (refusal.Length > 0) return (false, refusal);
            Closed = true;
            _history.Clear();
            _onClose?.Invoke();
            return (true, "");
        }

        /// <summary>JS cancel(): every editor-owned field back exactly as the session found it.</summary>
        public void Cancel()
        {
            Live();
            _snapshot.Restore(_run);
            Closed = true;
            _history.Clear();
            _onClose?.Invoke();
        }
    }
}
