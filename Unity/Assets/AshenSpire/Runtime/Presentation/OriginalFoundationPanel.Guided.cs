using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class OriginalFoundationPanel
    {
        private int _step, _previewCard;
        private bool _shapeValid = true;
        private static readonly string[] Steps = { "Choose your class", "Choose your attributes", "Choose your equipment", "Choose your relic", "Ready to climb", "Your weapon cards", "Optional customization" };
        private static readonly string[] StepTabs = { "Class", "Attributes", "Equipment", "Relic", "Review" };
        private void GuidedCreation()
        {
            _root.Clear(); _root.AddToClassList("original-creation"); _root.AddToClassList("creation-guided");
            _root.EnableInClassList("creation-card-preview", _step == 5);
            _root.EnableInClassList("creation-custom-step", _step == 6);
            _root.EnableInClassList("creation-wide", _root.contentRect.width >= 850);
            _root.style.height = Length.Percent(100); _root.style.minHeight = 0; _root.style.flexShrink = 1;
            var outer = _root.GetFirstAncestorOfType<ScrollView>();
            if (outer != null) { outer.contentContainer.style.height = Length.Percent(100); outer.scrollOffset = Vector2.zero; }
            _target = _root;
            var options = new OriginalStartingOptions(_catalog);
            var kits = options.AvailableKits(_creation.ClassId, _profileMeta);
            if (!kits.Any(x => (string)x["id"] == _kit && (bool?)x["available"] == true))
                _kit = (string)kits.First(x => (bool?)x["baseline"] == true && (bool?)x["available"] == true)["id"];
            var preview = _builder.Preview(_creation, _kit, _profileMeta, _starting);
            var hero = _catalog.Record("classes", _creation.ClassId);
            var relic = _catalog.Record("relics", (string)preview["relicId"]);
            void Go(int step) { _step = step; Creation(); }
            // JS build 1815 creator: a header ("Create character"), a step rail at the left
            // (a tab row on a phone), the step itself, and a Back / Next footer.
            var header = new VisualElement(); header.AddToClassList("creation-header"); _root.Add(header); _target = header;
            Label("Create character", "creation-title");
            Label(_step < 5 ? "PREPARE YOUR FORSAKEN · " + (_step + 1) + " / 5" : "PREPARE YOUR FORSAKEN · OPTIONAL", "creation-eyebrow");
            var main = new VisualElement(); main.AddToClassList("creation-main"); _root.Add(main);
            var rail = new VisualElement(); rail.AddToClassList("creation-rail"); main.Add(rail);
            var railStep = _step == 5 ? 2 : _step == 6 ? 4 : _step;
            for (var index = 0; index < StepTabs.Length; index++)
            {
                var step = index;
                var tab = MakeButton("native-creation-step-" + index, index == 0 ? StepTabs[0] + " · " + hero["name"] : StepTabs[index], () => Go(step));
                tab.AddToClassList("creation-rail-item"); tab.EnableInClassList("selected", index == railStep);
                tab.SetEnabled(index <= 1 || _creation.CanBegin); rail.Add(tab);
            }
            // The step scrolls inside the bounded sheet, so a long step never pushes the footer away.
            var body = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; body.AddToClassList("creation-step"); main.Add(body); _target = body;
            Label(Steps[_step], "heading");
            string Item(string hand) { var id = (string)preview["loadout"]["sets"][hand][0]; return string.IsNullOrEmpty(id) ? "Empty hand" : (string)_catalog.Record("equipment.armaments", id)["name"]; }
            if (_step == 0)
            {
                // One framed row per class: figure, name, description; the chosen row adds its
                // stat strip, starting arms and relic (JS .as-option.class-pick.cz-class).
                var choices = new VisualElement(); choices.AddToClassList("creation-class-grid"); body.Add(choices);
                foreach (var record in _catalog.Table("classes"))
                {
                    var id = (string)record["id"]; var chosen = id == _creation.ClassId;
                    var choice = MakeButton("foundation-class-" + id, "", () => { if (_creation.ClassId != id) { _creation.Select(id, _creation.ModeId); _starting.RemoveAll(); _kit = null; } Creation(); });
                    choice.AddToClassList("creation-class-row"); choice.EnableInClassList("selected", chosen); choice.tooltip = (string)record["name"];
                    var figure = new OriginalPlayerFigure(_setup); figure.UseOwnerBattleArt(id); figure.pickingMode = PickingMode.Ignore; figure.AddToClassList("creation-row-figure"); choice.Add(figure);
                    var copy = new VisualElement { pickingMode = PickingMode.Ignore }; copy.AddToClassList("creation-row-copy"); choice.Add(copy);
                    var name = new Label((string)record["name"]) { pickingMode = PickingMode.Ignore }; name.AddToClassList("creation-row-name"); copy.Add(name);
                    var description = new Label((string)record["description"]) { pickingMode = PickingMode.Ignore }; description.AddToClassList("creation-row-description"); copy.Add(description);
                    if (!chosen) continue;
                    ResourceStrip(copy, (JObject)preview["resources"]);
                    var kit = new Label(Item("rightHand") + " · " + Item("leftHand") + " · " + relic["name"]) { pickingMode = PickingMode.Ignore }; kit.AddToClassList("creation-row-relic"); copy.Add(kit);
                }
            }
            else if (_step == 1)
            {
                Choices("foundation-mode", "Allocation", CreationModel.VisibleModes(_catalog), _creation.ModeId, value => { if (_creation.ModeId != value) _creation.Select(_creation.ClassId, value); Creation(); });
                Label(_creation.ModeId == "leanStandard" ? "Standard is ready. Your class attributes are already assigned." : "Points remaining: " + _creation.Remaining, "caption");
                foreach (var attribute in _creation.Attributes().Properties())
                {
                    var id = attribute.Name; var row = new VisualElement(); row.AddToClassList("guided-attribute"); body.Add(row);
                    var title = new Label(AttributeName(id)); title.style.flexGrow = 1; row.Add(title);
                    if (_creation.ModeId != "leanStandard") { var minus = MakeButton("attribute-" + id + "-down", "−", () => { _creation.Adjust(id,-1); Creation(); }); minus.SetEnabled(_creation.CanAdjust(id,-1)); row.Add(minus); }
                    var value = new Label(attribute.Value.ToString()); value.AddToClassList("creation-attribute-value"); row.Add(value);
                    if (_creation.ModeId != "leanStandard") { var plus = MakeButton("attribute-" + id + "-up", "+", () => { _creation.Adjust(id,1); Creation(); }); plus.SetEnabled(_creation.CanAdjust(id,1)); row.Add(plus); }
                }
            }
            else if (_step == 2)
            {
                var objects = new VisualElement(); objects.AddToClassList("guided-item-row"); body.Add(objects);
                foreach (var hand in new[] { "rightHand", "leftHand" })
                {
                    var item = PlayerComponentArt.Item("weapon_" + (string)preview["loadout"]["sets"][hand][0], Item(hand));
                    if (item != null) objects.Add(item);
                }
                StartingChoice("native-start-kit", "Loadout", new JArray(kits.Where(x => (bool?)x["available"] == true)), _kit, value => { _kit = value; _starting.Remove("startingHands"); Creation(); });
                foreach (var hand in new[] { "rightHand", "leftHand" })
                {
                    var slot = hand; var rows = options.AvailableHands(_creation.ClassId,slot); rows.Insert(0,new JObject { ["id"]="", ["name"]="Empty hand" });
                    StartingChoice("native-start-" + slot, slot == "rightHand" ? "Weapon" : "Off hand", rows, (string)preview["loadout"]["sets"][slot][0], value =>
                    {
                        var hands = new JObject { ["rightHand"] = preview["loadout"]["sets"]["rightHand"][0].DeepClone(), ["leftHand"] = preview["loadout"]["sets"]["leftHand"][0].DeepClone() };
                        _starting["startingHands"] = OriginalStartingOptions.SelectHand(hands,slot,value); Creation();
                    });
                }
                StartingChoice("native-start-armour", "Armour", options.AvailableArmour(_creation.ClassId,_profileMeta), (string)preview["loadout"]["sets"]["armor"][0], value => { _starting["startingArmourId"] = value; Creation(); });
                Label("Load " + preview["weight"]["load"] + " / " + preview["weight"]["capacity"] + " · " + preview["weight"]["weightClass"]["id"], "caption");
                Button("native-preview-cards", "Preview weapon cards", () => Go(5));
            }
            else if (_step == 3)
            {
                StartingChoice("native-start-relic", "Starting relic", options.AvailableRelics(_creation.ClassId), (string)preview["relicId"], value => { _starting["startingRelicId"] = value; Creation(); });
                var relicArt = PlayerComponentArt.Item((string)preview["relicId"], (string)relic["name"]);
                if (relicArt != null) { relicArt.AddToClassList("guided-relic-art"); body.Add(relicArt); }
                Label((string)relic["name"], "node-title"); Label(RelicDescription(relic), "lead");
                Label("Your class's main relic is selected by default.", "caption");
            }
            else if (_step == 4)
            {
                Label((string)hero["name"], "node-title");
                Label((_creation.ModeId == "leanStandard" ? "Standard" : "Custom attributes") + "\n" + Item("rightHand") + " · " + Item("leftHand") + "\n" + relic["name"], "lead");
                ResourceStrip(body,(JObject)preview["resources"]);
                var seed = new TextField("Run seed") { name="native-seed", value=_seed }; seed.AddToClassList("foundation-field"); seed.RegisterValueChangedCallback(e => _seed=e.newValue); body.Add(seed);
                Button("native-customize", "Optional appearance and custom climb", () => Go(6));
            }
            else if (_step == 5)
            {
                var groups = preview["cards"].GroupBy(x => ((JObject)x["card"]).ToString()).ToArray();
                _previewCard = Mathf.Clamp(_previewCard,0,groups.Length-1); var group = groups[_previewCard]; var card = (JObject)group.First()["card"];
                var face = new OriginalCardView(_catalog,card,CardMechanics.CostProfile(card,(int?)relic["passives"]?["powerCostReduction"] ?? 0,(JObject)preview["weight"]["weightClass"]),null,false,()=>{},"native-preview-card-"+_previewCard);
                face.AddToClassList("guided-card"); body.Add(face);
                Label(group.Count() + " in starting deck · " + (_previewCard+1) + " / " + groups.Length, "caption");
                var row = new VisualElement(); row.AddToClassList("guided-navigation"); body.Add(row);
                var previous=MakeButton("native-preview-previous","Previous card",()=> { _previewCard--; Creation(); }); previous.SetEnabled(_previewCard>0); row.Add(previous);
                var next=MakeButton("native-preview-next","Next card",()=> { _previewCard++; Creation(); }); next.SetEnabled(_previewCard<groups.Length-1); row.Add(next);
            }
            else
            {
                var custom = new ScrollView(); custom.AddToClassList("guided-custom"); body.Add(custom);
                OriginalCustomSetupPanel.Render(custom,_catalog,_setup,_report,null,valid => _shapeValid=valid);
            }
            foreach (var requirement in preview["requirements"]) Label(requirement["itemId"] + " needs " + requirement["required"] + " " + requirement["attribute"],"notice");
            _target=_root;
            var footer = new VisualElement(); footer.AddToClassList("guided-footer"); _root.Add(footer);
            var navigation = new VisualElement(); navigation.AddToClassList("guided-navigation"); footer.Add(navigation);
            navigation.Add(MakeButton("native-creation-back",_step==0?"Back to title":"Back",()=> { if (_step==0) _back(); else Go(_step==5?2:_step==6?4:_step-1); }));
            // JS footer: Back at the left, the green Next at the right; Begin (available from
            // any step, as before) sits beside Next and turns green on Review.
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore }; spacer.AddToClassList("guided-spacer"); navigation.Add(spacer);
            var begin=MakeButton("native-begin","Begin the climb",()=>
            {
                if (!_shapeValid) return;
                var player=_builder.Build(_creation,_kit,_profileMeta,_starting);
                foreach(var property in _setup.Properties()) player[property.Name]=property.Value.DeepClone();
                _start(player,RandomStreams.ParseSeed(_seed));
            });
            begin.AddToClassList("primary"); begin.EnableInClassList("creation-begin-early", _step < 4); begin.SetEnabled((bool)preview["canBegin"] && _shapeValid); navigation.Add(begin);
            if (_step<4) { var next=MakeButton("native-creation-next","Next · " + new[] { "Attributes","Equipment","Relic","Ready" }[_step],()=>Go(_step+1)); next.AddToClassList("creation-next"); next.SetEnabled(_step!=1 || _creation.CanBegin); navigation.Add(next); }
            _report();
        }
    }
}
