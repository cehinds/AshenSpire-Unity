// RunSummaryView.cs — end-of-run screen (F11 / US-11.2), UI Toolkit only.
// PARITY: mirrors src/ui/screens/gameover.js (mountGameOver): page door
// ("The climb" / "The climb ends", "Victory" / "Defeat"), Title·L ("EMBER
// RESTORED" / "YOU PERISHED", loss tone), ornament, DetailCard (Forsaken, name
// + glyph, "Floor N / M · K fights won", "Seed S"), StatStrip (Damage dealt,
// Damage taken, Cinders, Final HP), "Earned" card, "Final deck" head + strip,
// and the ButtonRow ("Run history", "Return to title").
// INPUT: an immutable RunSummary (Domain/Original/Summary). No game rules,
// commands, saves or subscriptions here. Styles: Resources/RunSummary.uss.
// WIRING: OriginalRunPanel (solo) and OriginalCoopPanel (co-op) mount this in
// place of their old one-line Victory/Defeat text. The caller appends its own
// named buttons (native-deck, native-menu) to the returned button row.
using System;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public static class RunSummaryView
    {
        public const string StyleSheetName = "RunSummary";
        public const string RootName = "native-run-summary";
        public const string HistoryButtonName = "native-run-history";

        /// <summary>
        /// Adds the end-of-run door to <paramref name="parent"/> and returns its
        /// button row. <paramref name="history"/> null omits "Run history"
        /// (the HTML also omits it when there is no history handler).
        /// </summary>
        public static VisualElement Mount(VisualElement parent, RunSummary summary, Action history)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            var sheet = Resources.Load<StyleSheet>(StyleSheetName);
            if (sheet != null && !parent.styleSheets.Contains(sheet)) parent.styleSheets.Add(sheet);

            var door = new VisualElement { name = RootName };
            door.AddToClassList("run-summary");
            door.AddToClassList(summary.Victory ? "run-summary--victory" : "run-summary--loss");
            door.tooltip = summary.AriaLabel;
            parent.Add(door);

            // Page door head (modalHead): eyebrow + title.
            var head = Box(door, "run-summary-head");
            Text(head, Upper(summary.DoorEyebrow), "run-summary-door-eyebrow");
            Text(head, summary.DoorTitle, "run-summary-door-title");

            // decide(): Title·L, ornament, then the children in HTML order.
            var body = Box(door, "run-summary-body");
            var title = Text(body, summary.Title.ToUpperInvariant(), "run-summary-title");
            title.name = "native-run-summary-title";
            if (summary.TitleTone != null) title.AddToClassList("run-summary-title--" + summary.TitleTone);
            var ornament = Box(body, "original-ornament");
            ornament.AddToClassList("run-summary-ornament");
            Box(ornament, "original-rule"); Box(ornament, "original-gem"); Box(ornament, "original-rule");

            var card = Box(body, "run-summary-card");
            card.name = "native-run-summary-card";
            Text(card, Upper(summary.CardEyebrow), "run-summary-card-eyebrow");
            Text(card, summary.CardName, "run-summary-card-name");
            Text(card, summary.CardLine, "run-summary-card-line");
            Text(card, summary.CardMeta, "run-summary-card-meta").name = "native-run-summary-seed";

            var stats = Box(body, "run-summary-stats");
            stats.name = "native-run-summary-stats";
            foreach (var stat in summary.Stats)
            {
                var chip = Box(stats, "run-summary-chip");
                chip.name = "native-run-summary-stat-" + stat.Id;
                Text(chip, Upper(stat.Label), "run-summary-chip-key");
                Text(chip, stat.Value, "run-summary-chip-value");
            }

            if (summary.HasEarned)
            {
                var earned = Box(body, "run-summary-card");
                earned.AddToClassList("run-summary-earned");
                earned.name = "native-run-summary-earned";
                Text(earned, Upper(summary.EarnedEyebrow), "run-summary-card-eyebrow");
                foreach (var unlock in summary.Earned)
                {
                    var line = Box(earned, "run-summary-earned-line");
                    Text(line, unlock.Name, "run-summary-card-line");
                    Text(line, unlock.Kind, "run-summary-earned-kind");
                }
            }

            var deckHead = Box(body, "run-summary-section-head");
            Text(deckHead, Upper(summary.DeckEyebrow), "run-summary-card-eyebrow");
            Text(deckHead, summary.DeckTitle, "run-summary-deck-title");
            var deck = Box(body, "run-summary-deck");
            deck.name = "native-run-summary-deck";
            foreach (var entry in summary.Deck)
            {
                var item = Box(deck, "run-summary-deck-item");
                if (entry.Upgraded) item.AddToClassList("run-summary-deck-item--upgraded");
                Text(item, entry.Glyph, "run-summary-deck-glyph");
                Text(item, entry.Name, "run-summary-deck-name");
            }

            var buttons = Box(body, "run-summary-buttons");
            buttons.name = "native-run-summary-buttons";
            if (history != null)
            {
                var toHistory = new Button(history) { text = summary.HistoryLabel, name = HistoryButtonName };
                toHistory.AddToClassList("button");
                buttons.Add(toHistory);
            }
            return buttons;
        }

        /// <summary>
        /// Co-op: builds the member's summary from a completed OriginalCoopRun
        /// view (scene kind "complete"). Returns null when the view has no
        /// finished local run, so the caller can keep its plain fallback text.
        /// </summary>
        public static RunSummary FromCoopView(JObject view, OriginalContentCatalog catalog)
        {
            if (!(view?["local"]?["run"] is JObject local) || catalog == null) return null;
            var run = (JObject)local.DeepClone();
            if (string.IsNullOrEmpty((string)run["phase"]) || !Enum.TryParse<OriginalRunPhase>((string)run["phase"], out var phase) || !RunSummary.IsTerminal(phase))
                run["phase"] = (string)view["scene"]?["result"] == "victory" ? "Victory" : "Defeat";
            if (string.IsNullOrEmpty((string)run["seedString"]) && view["seedString"] != null) run["seedString"] = view["seedString"].DeepClone();
            try { return RunSummary.FromRun(run, catalog); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is InvalidCastException || error is NullReferenceException)
            { Debug.LogWarning("Co-op run summary unavailable: " + error.Message); return null; }
        }

        // HTML eyebrows and chip keys use CSS text-transform: uppercase.
        private static string Upper(string text) => (text ?? "").ToUpperInvariant();
        private static VisualElement Box(VisualElement parent, string style)
        {
            var box = new VisualElement(); box.AddToClassList(style); parent.Add(box); return box;
        }
        private static Label Text(VisualElement parent, string text, string style)
        {
            var label = new Label(text ?? ""); label.AddToClassList(style); parent.Add(label); return label;
        }
    }
}
