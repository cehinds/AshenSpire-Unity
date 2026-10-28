// Core2RunHost.Doors.cs — the spoils door over the map (JS showGameProfileReward: the victory receipt with its
// Progression panel, the level notices, then the mixed offer with Inspect / Confirm → Add to deck · Keep in collection ·
// Edit deck / Skip) and the finished-run door (JS mountGameOver). Words and rows: Core2SpoilsModel, Core2FinishModel.
using System;
using System.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2RunHost
    {
        private bool _inspecting;

        private void BuildSpoils(VisualElement screen)
        {
            if (_spoils == null) _spoils = new Core2SpoilsModel(_flow); else _spoils.Refresh();
            var m = _spoils;
            if (m.Stage == null) return;
            var door = Core2Ui.Door(m.Eyebrow, m.Title, null, wide: m.Stage == "offer");
            switch (m.Stage)
            {
                case "victoryReceipt":
                {
                    door.Body.Add(Core2Ui.Text(m.ProgressHeading, "c2-micro"));
                    foreach (var row in m.Progress)
                    {
                        var line = Core2Ui.Div("c2-row c2-progress-row");
                        var label = Core2Ui.Div("c2-col c2-fill");
                        label.Add(Core2Ui.Text(row.Label, "c2-strong"));
                        label.Add(Core2Ui.Text(row.GainedText, "c2-caption"));
                        line.Add(label);
                        line.Add(Core2Ui.Text(row.LevelText, "c2-gold"));
                        var bar = Core2Ui.Div("c2-progress-bar");
                        var fill = Core2Ui.Div("c2-progress-fill");
                        fill.style.width = Length.Percent((float)(row.Fraction * 100));
                        bar.Add(fill);
                        bar.Add(Core2Ui.Text(row.BarText, "c2-progress-text"));
                        line.Add(bar);
                        line.Add(Core2Ui.Text(row.NextText, ""));
                        door.Body.Add(line);
                    }
                    door.Foot.Add(Core2Ui.Text(m.FootNote, "c2-door-note"));
                    door.Foot.Add(Core2Ui.Btn("reward-continue", m.ContinueLabel, () => Run(m.Acknowledge), "c2-btn-primary c2-btn-fill"));
                    break;
                }
                case "levelNotices":
                {
                    foreach (var (title, lines, next, before, after) in m.Notices)
                    {
                        var section = Core2Ui.Div("c2-sheet");
                        section.Add(Core2Ui.Text(title, "c2-strong c2-text"));
                        var bar = Core2Ui.Div("c2-progress-bar");
                        bar.style.width = Length.Percent(100);
                        var fill = Core2Ui.Div("c2-progress-fill");
                        fill.style.width = Length.Percent((float)(after * 100));
                        bar.Add(fill);
                        section.Add(bar);
                        foreach (var l in lines) section.Add(Core2Ui.Text("• " + l, "c2-text"));
                        if (next != null) section.Add(Core2Ui.Text(next, "c2-caption"));
                        door.Body.Add(section);
                    }
                    door.Foot.Add(Core2Ui.Btn("reward-level-continue", m.ContinueLabel, () => Run(m.Acknowledge), "c2-btn-primary c2-btn-fill"));
                    break;
                }
                default:
                {
                    door.Body.Add(Core2Ui.Text(m.Note, "c2-text c2-muted"));
                    var grid = Core2Ui.Div("c2-row c2-wrap");
                    foreach (var option in m.Options)
                    {
                        if (m.Reviewing && option.Index != m.Selected) continue;
                        var index = option.Index;
                        var tile = Core2Ui.Tile(OfferArt(option.Kind), option.Name, new[] { option.KindLabel, option.Text }, option.Unavailable, m.Selected == index);
                        tile.style.width = Length.Percent(m.Reviewing || Narrow ? 100 : 48); // one column on the phone
                        var pick = Core2Ui.Btn("reward-pick-" + index, option.Name, () => { m.Select(index); _inspecting = false; Render(); }, "c2-btn-small", !option.Unavailable && !m.Reviewing);
                        tile.Add(pick);
                        grid.Add(tile);
                    }
                    door.Body.Add(grid);
                    if (_inspecting && m.Selected.HasValue)
                    {
                        var o = m.Options[m.Selected.Value];
                        var sheet = Core2Ui.Div("c2-sheet");
                        sheet.Add(Core2Ui.Text(o.Name, "c2-title-s"));
                        sheet.Add(Core2Ui.Text(o.KindLabel, "c2-micro"));
                        sheet.Add(Core2Ui.Text(o.Text, "c2-text"));
                        door.Body.Add(sheet);
                    }
                    door.Body.Add(Core2Ui.Text(m.Status, "c2-status"));
                    door.Foot.Add(Core2Ui.Btn("reward-continue", m.SkipLabel, () => { _inspecting = false; Run(m.Skip); if (m.Reviewing || _flow.Screen == AshenSpire.Domain.Core2.Core2Screen.Reward) Render(); }, m.Reviewing ? "" : "c2-btn-danger"));
                    door.Foot.Add(Core2Ui.Btn("reward-inspect", m.InspectLabel, () => { _inspecting = !_inspecting; Render(); }, "", m.CanInspect));
                    if (m.ShowEditDeck) door.Foot.Add(Core2Ui.Btn("reward-edit-deck", m.EditDeckLabel, () => { Run(m.Keep); Open(Overlay.Deck); }));
                    if (m.ShowKeep) door.Foot.Add(Core2Ui.Btn("reward-keep", m.KeepLabel, () => Run(m.Keep)));
                    door.Foot.Add(Core2Ui.Btn("reward-card-confirm", m.ConfirmLabel, () => { _inspecting = false; Run(m.Confirm); Render(); }, "c2-btn-primary c2-btn-fill", m.CanConfirm));
                    break;
                }
            }
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door, m.Stage == "offer" ? "reward-card-confirm" : null);
        }

        private static string OfferArt(string kind) => kind switch
        {
            "combatCards" or "weaponArts" => "Art/js1815/ui/icon-deck",
            "weapons" => "Art/js1815/ui/icon-attack",
            "armor" => "Art/js1815/ui/icon-equipment",
            "relics" => "Art/js1815/ui/icon-relic",
            "potions" => "Art/js1815/ui/icon-flask-health",
            _ => "Art/js1815/ui/icon-reward",
        };

        private void BuildFinish(VisualElement screen)
        {
            var m = new Core2FinishModel(_flow);
            var vista = Core2Ui.Div("c2-backdrop");
            Core2Ui.Surface(vista, _root.ClassListContains("layout-narrow") ? "Art/js1815/ui/scene-spire-vista-mobile" : "Art/js1815/ui/scene-spire-vista-desktop");
            screen.Add(vista);
            var door = Core2Ui.Door(m.Eyebrow.ToUpperInvariant(), m.Title);
            var heading = Core2Ui.Text(m.Heading, "c2-title-l");
            if (!m.Victory) heading.AddToClassList("c2-ember");
            door.Body.Add(heading);
            var identity = Core2Ui.Div("c2-sheet");
            identity.Add(Core2Ui.Text(m.IdentityEyebrow.ToUpperInvariant(), "c2-micro"));
            identity.Add(Core2Ui.Text(m.Name, "c2-title-s"));
            identity.Add(Core2Ui.Text(m.Line, ""));
            identity.Add(Core2Ui.Text(m.Meta, "c2-caption"));
            door.Body.Add(identity);
            var chips = Core2Ui.Div("c2-row c2-wrap c2-gap");
            foreach (var (key, value) in m.Chips)
            {
                var chip = Core2Ui.Div("c2-col c2-sheet");
                chip.Add(Core2Ui.Text(key, "c2-micro"));
                chip.Add(Core2Ui.Text(value, "c2-strong"));
                chips.Add(chip);
            }
            door.Body.Add(chips);
            if (m.Earned.Count > 0)
            {
                var earned = Core2Ui.Div("c2-sheet");
                earned.Add(Core2Ui.Text(m.EarnedLabel.ToUpperInvariant(), "c2-micro"));
                foreach (var e in m.Earned) earned.Add(Core2Ui.Text(e, "c2-gold"));
                door.Body.Add(earned);
            }
            door.Body.Add(Core2Ui.Text(m.DeckEyebrow.ToUpperInvariant(), "c2-micro"));
            door.Body.Add(Core2Ui.Text(m.DeckTitle, "c2-title-s"));
            door.Body.Add(Core2Ui.Text(string.Join(" · ", m.Deck), "c2-text c2-muted"));
            door.Foot.Add(Core2Ui.Btn("run-again", m.RunAgainLabel, () => RunAgain(m.ClassId), "c2-btn-primary"));
            door.Foot.Add(Core2Ui.Btn("to-title", m.TitleLabel, () => { var exit = _exit; Dispose(); _flow.Release(); exit?.Invoke(null); }));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door, "run-again");
        }

        /// <summary>JS Run again: a NEW climb, same class, a new random seed, in the same slot.</summary>
        private void RunAgain(string classId)
        {
            var slot = Math.Max(0, _flow.Slot);
            var seed = Core2Seed.ToText(unchecked((uint)Environment.TickCount ^ (uint)DateTime.UtcNow.Ticks));
            if (!_flow.ClassAvailable(classId)) classId = _flow.Content.Classes.First(c => _flow.ClassAvailable(c.Id)).Id;
            Run(() => _flow.Begin(classId, seed, slot));
        }
    }
}
