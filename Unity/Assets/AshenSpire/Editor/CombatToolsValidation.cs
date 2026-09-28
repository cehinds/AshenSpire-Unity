// Unity-compiled UI callback fixtures for retained-card prompts, which shipped
// defaults do not normally open. This complements (not replaces) the real-input
// Web playtest. Run with -executeMethod AshenSpire.Editor.CombatToolsValidation.Run.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Editor
{
    public static class CombatToolsValidation
    {
        public static void Run()
        {
            var checks = new JArray();
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException("Combat UI fixture failed: " + label);
                checks.Add(label);
            }
            var directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../GameContent/Unity/Original"));
            var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(directory, "content.json")));
            var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(directory, "mechanics.json")));
            var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(directory, "progression.json"))));
            var supplement = JObject.Parse(File.ReadAllText(Path.Combine(directory, "event-choices.json")));
            var creator = new CreationModel(catalog, "starseer", "leanStandard", progression);
            var kit = (string)catalog.Table("equipment.startingKits").First(row => (string)row["classId"] == "starseer" && (bool?)row["baseline"] == true)["id"];
            var player = new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit);
            var map = OriginalGameSession.Start(catalog, supplement, mechanics, player, 7);
            OriginalGameSession opening = null;
            foreach (var id in map.LegalNodeIds)
            {
                var candidate = OriginalGameSession.Restore(map.Snapshot()); candidate.Enter(id);
                if (candidate.Phase == OriginalRunPhase.Combat) { opening = candidate; break; }
            }
            Check(opening != null, "authored seeded opening has combat");
            foreach (var forced in new[] { false, true })
            {
                var saved = opening.Snapshot();
                var rules = (JObject)saved["run"]["room"]["combatSnapshot"]["handRules"];
                rules["promptDiscard"] = !forced; rules["discardLimit"] = 2;
                if (forced)
                {
                    rules["overflow"] = "discard";
                    rules["capacity"] = new JObject { ["base"] = opening.Hand.Count - 2, ["statEnabled"] = false, ["stat"] = "intelligence", ["baseline"] = 1, ["pointsPerCard"] = 1, ["minimum"] = 1, ["maximum"] = 30 };
                }
                var game = OriginalGameSession.Restore(saved);
                var holder = new VisualElement(); var root = new VisualElement(); var changes = 0;
                void Mount()
                {
                    holder.Clear(); root = new VisualElement(); holder.Add(root);
                    _ = new OriginalRunPanel(root, holder, game, () => { }, () => { }, false);
                }
                game.Changed += () => { changes++; Mount(); }; Mount();
                void Click(string id)
                {
                    var button = holder.Q<Button>(id);
                    Check(button != null && button.enabledInHierarchy, "fixture can activate " + id);
                    // Invoke the existing registered callback, without replacing handlers
                    // or bypassing domain commands. No physical-input claim is made here.
                    typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Invoke(button.clickable, new object[] { null });
                }
                var before = game.Snapshot();
                Click("native-end-turn");
                Check(holder.Q<Button>("native-discard-confirm") != null, "end turn opens " + (forced ? "required" : "optional") + " prompt");
                Check(holder.Q<Button>("native-discard-confirm").enabledInHierarchy == !forced, "minimum governs empty confirmation");
                Check(JToken.DeepEquals(game.Snapshot(), before) && changes == 0, "opening chooser leaves state and RNG unchanged");
                Click("native-discard-cancel");
                Check(JToken.DeepEquals(game.Snapshot(), before) && changes == 0 && holder.Q<Button>("native-end-turn") != null, "cancel returns to combat without a command");
                Click("native-end-turn");
                var ids = game.DiscardPlan["cardIds"].Values<string>().Take(3).ToArray();
                var firstChoiceControl = holder.Q<Button>("native-discard-choice-" + ids[0]);
                Click("native-discard-choice-" + ids[0]);
                Check(ReferenceEquals(firstChoiceControl, holder.Q<Button>("native-discard-choice-" + ids[0])), "selection keeps existing controls for stable focus and scrolling");
                Check(holder.Q<Button>("native-discard-confirm").enabledInHierarchy == !forced, "one selected respects forced minimum of two");
                Click("native-discard-choice-" + ids[1]);
                Check(holder.Q<Button>("native-discard-confirm").enabledInHierarchy, "two selected can confirm");
                Check(!holder.Q<Button>("native-discard-choice-" + ids[2]).enabledInHierarchy, "maximum disables additional choices");
                Click("native-discard-choice-" + ids[0]);
                Check(holder.Q<Button>("native-discard-choice-" + ids[2]).enabledInHierarchy, "deselecting reopens other choices");
                Click("native-discard-choice-" + ids[0]);
                Check(JToken.DeepEquals(game.Snapshot(), before) && changes == 0, "selection edits remain presentation-only");
                Click("native-discard-confirm");
                Check(changes == 1 && game.Turn == 2, "confirmation commits exactly one turn");
                var discarded = game.LastEvents.Where(e => (string)e["type"] == "cardDiscarded" && (string)e["reason"] == "choice").Select(e => (string)e["cardInstanceId"]).ToArray();
                Check(discarded.SequenceEqual(ids.Take(2)), "only selected cards are discarded, in hand order despite toggling");
                Check(holder.Q<Button>("native-discard-confirm") == null, "application refresh removes the submitted prompt");
                Check(JToken.DeepEquals(OriginalGameSession.Restore(game.Snapshot()).Snapshot(), game.Snapshot()), "post-choice save restores exactly");
            }
            var output = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../Builds/combat-tools-fixtures.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, new JObject { ["passed"] = true, ["checks"] = checks, ["physicalInput"] = false, ["scope"] = "Unity-compiled callback fixtures using modified saved hand rules; not a shipped-default playthrough." }.ToString());
            Debug.Log("Combat UI fixtures: " + checks.Count + " checks passed");
        }
    }
}
