// Core2SpoilsModel.cs — the spoils door in its three stages (JS src/main.js showGameProfileReward):
//   victoryReceipt  src/ui/screens/reward.js mountRewards({ receiptOnly }): the fight's XP panel, then Continue
//   levelNotices    src/ui/components/gameProfileLevelUp.js: one summary per track that levelled, then Continue
//   offer           src/ui/components/gameProfileOffers.js: pick one of the options, Inspect, Confirm → review
//                   (Add to deck / Keep in collection / Edit deck, or Continue for a non-card), Skip
// Engine-free. The rows, the option faces and the review state live here; Core2SpoilsView only draws them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>One line of the receipt's Progression panel (JS rewardProgress rows).</summary>
    public sealed class Core2ProgressRow
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string LevelText { get; set; }
        public string NextText { get; set; }
        public string BarText { get; set; }
        public double Fraction { get; set; }
        public string GainedText { get; set; }
    }

    /// <summary>One option of a mixed offer (a card, art, armament, armour, relic or flask), as its face reads.</summary>
    public sealed class Core2OfferOption
    {
        public int Index { get; set; }
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string KindLabel { get; set; }
        public string Text { get; set; }
        public bool Unavailable { get; set; }
        public bool IsCard => Kind == "combatCards" || Kind == "weaponArts";
        public bool IsEquipment => Kind == "weapons" || Kind == "armor";
    }

    public static class Core2Offers
    {
        /// <summary>The shelf label of an offer kind (JS mountConfiguredShop labels).</summary>
        public static string KindLabelId(string kind) => kind switch
        {
            "combatCards" => "shop.bar.combatCards", "weaponArts" => "shop.bar.weaponArts", "weapons" => "shop.bar.armaments",
            "armor" => "shop.bar.armour", "relics" => "shop.bar.relics", "potions" => "shop.bar.flasks", _ => "shop.title",
        };

        /// <summary>The option's definition name and glance text (JS: registries.cards/relics/flasks/equipment by kind).</summary>
        public static Core2OfferOption Option(Core2RunData data, Core2Run run, Core2RewardOffer offer, int index)
        {
            var o = offer.Options[index];
            string name = o.Id, text = "";
            switch (o.Kind)
            {
                case "combatCards":
                case "weaponArts":
                    if (data.Content.HasCard(o.Id)) { name = data.Content.Card(o.Id).Name; text = Core2CardText.Card(data.Content, o.Id); }
                    break;
                case "relics":
                    if (data.HasRelic(o.Id)) { name = (string)data.Relic(o.Id)["name"]; text = Core2CardText.Relic(data, o.Id); }
                    break;
                case "potions":
                    if (data.HasFlask(o.Id)) { name = (string)data.Flask(o.Id)["name"]; text = Core2CardText.Flask(data, o.Id); }
                    break;
                case "weapons":
                    var piece = data.Armament(o.Id);
                    if (piece != null) { name = (string)piece["name"]; text = (string)piece["blurb"] ?? ""; }
                    break;
                case "armor":
                    var armour = data.Armour(run.Class, o.Id);
                    if (armour != null) { name = (string)armour["name"] ?? o.Id; text = (string)armour["blurb"] ?? ""; }
                    break;
            }
            return new Core2OfferOption
            {
                Index = index, Kind = o.Kind, Id = o.Id, Name = name, KindLabel = Core2Text.T(KindLabelId(o.Kind)), Text = text,
                Unavailable = offer.Taken.Contains(index) || offer.Claimed.HasValue,
            };
        }
    }

    public sealed class Core2SpoilsModel
    {
        public string Stage { get; private set; }
        public string Eyebrow { get; private set; }
        public string Title { get; private set; }
        public string Note { get; private set; }
        public string FootNote { get; private set; }
        public List<Core2ProgressRow> Progress { get; } = new List<Core2ProgressRow>();
        public string ProgressHeading => Core2Text.T("reward.progress.heading").ToUpperInvariant();
        /// <summary>levelNotices: per track the title, its lines and the next-benefit line.</summary>
        public List<(string Title, List<string> Lines, string Next, double Before, double After)> Notices { get; } = new List<(string, List<string>, string, double, double)>();
        public List<Core2OfferOption> Options { get; } = new List<Core2OfferOption>();
        public string ContinueLabel { get; private set; }

        // ---- the offer's selection and review (JS mountGameProfileOffers locals) ---------------------------------
        public int? Selected { get; private set; }
        public bool Reviewing { get; private set; }
        public bool AddToDeckMode { get; private set; }
        public string Status { get; private set; } = "";
        private readonly Core2RunFlow _flow;
        private Core2RewardOffer _offer;

        public Core2SpoilsModel(Core2RunFlow flow)
        {
            _flow = flow;
            Refresh();
        }

        public void Refresh()
        {
            var session = _flow.Session;
            var data = _flow.Data;
            var pending = session.Run.PendingReward;
            Stage = session.RewardStage;
            Progress.Clear(); Notices.Clear(); Options.Clear();
            if (Stage == "victoryReceipt")
            {
                var receipt = pending.VictoryReceipt;
                Eyebrow = Core2Text.T("reward.eyebrow.spoils").ToUpperInvariant();
                Title = (string)receipt["title"] ?? Core2Text.T("reward.title.victory");
                FootNote = "Experience earned in this battle.";
                ContinueLabel = Core2Text.T("reward.continue");
                BuildProgress(data, session.Run, receipt["xpGains"] as JObject);
            }
            else if (Stage == "levelNotices")
            {
                Eyebrow = Core2Text.T("reward.level.eyebrow").ToUpperInvariant();
                Title = Core2Text.T("reward.level.button");
                ContinueLabel = Core2Text.T("common.continue");
                foreach (var row in pending.Progression.OfType<JObject>())
                {
                    var weapons = Core2Json2.Strings(row["weapons"]);
                    var label = (string)row["label"];
                    var before = (int)row["before"]; var after = (int)row["after"];
                    var title = (weapons.Count > 0 ? string.Join(", ", weapons) + " (" + label + " group)" : label) + " · Level " + before + " → " + after;
                    var effects = row["effects"] as JObject;
                    var changes = (effects?["changes"] as JArray)?.OfType<JObject>().Select(c => (string)c["label"] + ": " + c["before"] + " → " + c["after"]).ToList() ?? new List<string>();
                    if (changes.Count == 0) changes.Add(effects == null || effects["unavailable"] != null ? "Effect details were not saved for this level." : "No stat bonus or unlock at this level.");
                    var cap = Core2Json2.Int(effects?["maxLevel"]);
                    var next = effects?["nextMilestone"] is JObject m ? "Next benefit at level " + m["level"] + ": " + string.Join("; ", Core2Json2.Strings(m["labels"])) : null;
                    Notices.Add((title, changes, next, cap > 0 ? before / (double)cap : 0, cap > 0 ? after / (double)cap : 0));
                }
            }
            else if (Stage == "offer")
            {
                _offer = session.CurrentOffer;
                Eyebrow = "";
                Title = pending.Source == "level" ? "Level reward" : "Victory rewards";
                var opensEditor = Core2RunArmoury.OpensDeckEditor(data, session.DeckSettings);
                Note = Core2Text.T(!Core2RunArmoury.NoncombatEditing(data) ? "reward.configured.note" : opensEditor ? "reward.configured.noteArmoury" : "reward.configured.noteNoEditor", new { count = _offer.TakeCount });
                for (var i = 0; i < _offer.Options.Count; i++) Options.Add(Core2Offers.Option(data, session.Run, _offer, i));
                ContinueLabel = Core2Text.T("common.skip");
            }
        }

        /// <summary>JS rewardProgress over the receipt's gains: the class track first, Character after it, then the paid tracks.</summary>
        private void BuildProgress(Core2RunData data, Core2Run run, JObject gains)
        {
            var level = Core2Json2.Int(gains?["level"]);
            var tracks = gains?["tracks"] as JObject ?? new JObject();
            var rows = new List<(string Kind, Core2ProgressRow Row, int Level, string Label)>();
            foreach (var track in ((JArray)data.Skill["tracks"]).OfType<JObject>())
            {
                var id = (string)track["id"]; var kind = (string)track["kind"];
                if (kind == "class" && id != Core2RunSkills.ClassSkillId(run.Class)) continue;
                var gained = Core2Json2.Int(tracks[id]);
                if (gained <= 0) continue;
                run.Skills.TryGetValue(id, out var ledger);
                var lv = ledger?.Level ?? 0; var xp = ledger?.Xp ?? 0;
                var next = Core2RunSkills.XpToNext(data, kind, lv);
                if (next <= 0) continue;
                var cap = Core2RunSkills.MaxLevel(data, kind);
                var capped = cap.HasValue && lv >= cap.Value;
                var label = (string)track["label"] ?? id;
                rows.Add((kind, new Core2ProgressRow
                {
                    Id = id, Label = label, LevelText = Core2Text.T("reward.progress.level", new { level = lv }),
                    NextText = capped ? Core2Text.T("reward.progress.capped") : Core2Text.T("reward.progress.next", new { level = lv + 1 }),
                    BarText = Math.Min(xp, capped ? xp : next) + " / " + (capped ? xp : next), Fraction = capped ? 1 : Math.Max(0, Math.Min(1, xp / (double)next)),
                    GainedText = Core2Text.T("reward.progress.gained", new { xp = gained }),
                }, lv, label));
            }
            rows = rows.OrderBy(r => r.Kind == "class" ? 0 : 1).ThenByDescending(r => r.Level).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
            var output = rows.Select(r => r.Row).ToList();
            if (level > 0)
            {
                var ledger = run.Level;
                var next = Core2RunProgression.XpToNext(data, ledger.Level);
                var character = new Core2ProgressRow
                {
                    Id = "character", Label = Core2Text.T("reward.progress.character"), LevelText = "Level " + ledger.Level, NextText = "Level " + (ledger.Level + 1),
                    BarText = Math.Min(ledger.Xp, next) + " / " + next, Fraction = next > 0 ? Math.Max(0, Math.Min(1, ledger.Xp / (double)next)) : 0,
                    GainedText = Core2Text.T("reward.progress.gained", new { xp = level }),
                };
                var at = output.Count > 0 && rows[0].Kind == "class" ? 1 : 0;
                output.Insert(at, character);
            }
            Progress.AddRange(output);
        }

        // ---- the commands ----------------------------------------------------------------------------------------

        /// <summary>Continue on the receipt or the notices stage (each is shown once).</summary>
        public string Acknowledge() => Stage == "victoryReceipt" ? _flow.AcknowledgeReceipt() : Stage == "levelNotices" ? _flow.AcknowledgeNotices() : null;

        public void Select(int index)
        {
            if (Reviewing || Stage != "offer" || index < 0 || index >= Options.Count || Options[index].Unavailable) return;
            Selected = index; Status = "";
        }

        public bool CanConfirm => Selected.HasValue && (!Reviewing || !AddToDeckMode || _refusal.Length == 0);
        public bool CanInspect => Selected.HasValue;
        private string _refusal = "";

        /// <summary>The primary button's label: Confirm, then in review Add to deck (a card) or Continue.</summary>
        public string ConfirmLabel => !Reviewing ? Core2Text.T("common.confirm") : AddToDeckMode ? Core2Text.T("reward.configured.addToDeck") : Core2Text.T("common.continue");
        /// <summary>The secondary button: Skip, or Back while reviewing.</summary>
        public string SkipLabel => Reviewing ? Core2Text.T("common.back") : Core2Text.T("common.skip");
        public bool ShowKeep => Reviewing && AddToDeckMode && !ShowEditDeck;
        public bool ShowEditDeck => Reviewing && AddToDeckMode && Core2RunArmoury.OpensDeckEditor(_flow.Data, _flow.Session.DeckSettings);
        public string KeepLabel => Core2Text.T("reward.configured.keep");
        public string EditDeckLabel => Core2Text.T("reward.configured.editDeck");
        public string InspectLabel => Core2Text.T("deckEditor.inspectAction");

        /// <summary>JS confirm.click: first a review of the one picked option, then the claim.</summary>
        public string Confirm()
        {
            if (!Selected.HasValue) return null;
            if (!Reviewing)
            {
                Reviewing = true;
                AddToDeckMode = Options[Selected.Value].IsCard;
                _refusal = AddToDeckMode ? _flow.Session.RewardDeckAddRefusal(Selected.Value) : "";
                Status = _refusal.Length == 0 ? "" : _refusal == "full"
                    ? Core2Text.T("reward.configured.deckFull", new { max = Core2RunDeck.Bounds(_flow.Session.DeckSettings).Max?.ToString(CultureInfo.InvariantCulture) ?? "" })
                    : Core2Text.T("reward.configured.deckLimit");
                return null;
            }
            return Commit(AddToDeckMode);
        }

        /// <summary>Keep in collection (or Edit deck's claim): the option is claimed without joining the deck.</summary>
        public string Keep() => Reviewing ? Commit(false) : null;

        /// <summary>Back (in review) or Skip (JS next.click: onContinue).</summary>
        public string Skip()
        {
            if (Reviewing) { Reviewing = false; AddToDeckMode = false; Status = ""; return null; }
            var error = _flow.ContinueReward();
            if (error != null) Status = error;
            return error;
        }

        private string Commit(bool toDeck)
        {
            var index = Selected.Value;
            var error = toDeck ? _flow.TakeRewardToDeck(index) : _offer.Taken.Contains(index) ? null : _flow.TakeReward(index);
            if (error != null) { Status = error; return error; }
            // JS proceed(): leave when the offer is spent, else show the next pick.
            var offer = _flow.Session.CurrentOffer;
            Reviewing = false; AddToDeckMode = false; Selected = null; Status = "";
            if (offer == null || offer.Claimed.HasValue || offer.Taken.Count >= Math.Min(offer.TakeCount, offer.Options.Count))
                return _flow.ContinueReward();
            Refresh();
            return null;
        }
    }
}
