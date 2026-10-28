// Core2CreationScreen.cs — Create character for a Core 2 run (JS showCustomize with the game profile's class-selection
// module; desktop-02c/02d/02e): a Class step (locked classes from the profile ledger read "Play 1 game to unlock."), a
// Review step (seed field, destination slot, the class's fixed six points and pools, armour, main hand, relic, the
// starting deck), Back / Next / Begin. When every slot holds a climb, a slot step comes first (JS title.slots door).
// Words and rules: Core2CreationModel; the run starts through Core2RunFlow.Begin.
using System;
using System.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2CreationScreen
    {
        private readonly ScreenRouter _router;
        private readonly VisualElement _root;
        private readonly Core2RunFlow _flow;
        private readonly Action _begun;
        private readonly Action _back;
        private Core2CreationModel _model;
        private bool _pickSlot;

        /// <param name="slot">The slot the climb will use, or -1 to choose one (every slot is full).</param>
        public Core2CreationScreen(ScreenRouter router, VisualElement root, Core2RunFlow flow, int slot, uint randomSeed, Action begun, Action back)
        {
            _router = router; _root = root; _flow = flow; _begun = begun; _back = back;
            _pickSlot = slot < 0;
            _model = new Core2CreationModel(flow, Math.Max(0, slot), randomSeed);
            Core2Ui.Style(root);
        }

        public void Show()
        {
            var body = _router.Show("core2:create");
            ScreenRouter.SetFixed(body, true);
            var screen = Core2Ui.Div("c2-screen c2-fill");
            body.Add(screen);
            var vista = Core2Ui.Div("c2-backdrop");
            Core2Ui.Surface(vista, _root.ClassListContains("layout-narrow") ? "Art/js1815/ui/scene-spire-vista-mobile" : "Art/js1815/ui/scene-spire-vista-desktop");
            screen.Add(vista);
            if (_pickSlot) { Slots(screen); return; }
            var m = _model;
            var door = Core2Ui.Door("", m.Title, null, wide: true);
            var steps = Core2Ui.Div("c2-tabs");
            steps.Add(Core2Ui.Btn("create-step-class", m.ClassStep + (m.Selected != null ? "  " + m.Classes.First(c => c.Id == m.Selected).Name : ""), () => { m.Back(); Show(); }, "c2-tab" + (m.Step == "class" ? " is-on" : "")));
            steps.Add(Core2Ui.Btn("create-step-review", m.ReviewStep, () => { m.Next(); Show(); }, "c2-tab" + (m.Step == "review" ? " is-on" : ""), m.Selected != null));
            door.Door.Insert(1, steps);
            if (m.Step == "class") ClassStep(door.Body);
            else ReviewStep(door.Body);
            door.Body.Add(Core2Ui.Text(m.Status, "c2-status"));
            door.Foot.Add(Core2Ui.Btn("create-back", m.BackLabel, () => { if (m.Step == "review") { m.Back(); Show(); } else _back?.Invoke(); }));
            if (m.Step == "class") door.Foot.Add(Core2Ui.Btn("create-next", m.NextLabel, () => { m.Next(); Show(); }, "c2-btn-primary", m.Selected != null));
            else door.Foot.Add(Core2Ui.Btn("create-begin", m.BeginLabel, Begin, "c2-btn-primary", true, m.Review().BeginTip));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door, m.Step == "class" ? "create-next" : "create-begin");
        }

        private void ClassStep(VisualElement body)
        {
            var m = _model;
            foreach (var cls in m.Classes)
            {
                var id = cls.Id;
                var lines = cls.Locked ? new[] { cls.LockText } : new[] { cls.Description, cls.Training, string.Join("   ", cls.Stats.Select(s => s.Key + " " + s.Value)), cls.Relic };
                var tile = Core2Ui.Tile(cls.Locked ? "Art/js1815/ui/icon-lock" : "Art/js1815/ui/icon-attack", cls.Locked ? "" : cls.Name, lines, cls.Locked, m.Selected == id);
                tile.Add(Core2Ui.Btn("create-class-" + id, cls.Locked ? cls.LockText : cls.Name, () => { m.Choose(id); Show(); }, "c2-btn-small", !cls.Locked));
                body.Add(tile);
            }
        }

        private void ReviewStep(VisualElement body)
        {
            var m = _model;
            var review = m.Review();
            var seed = Core2Ui.Div("c2-row c2-sheet");
            var seedText = Core2Ui.Div("c2-col c2-fill");
            seedText.Add(Core2Ui.Text(m.SeedLabel, "c2-strong"));
            seedText.Add(Core2Ui.Text(m.SeedHint, "c2-caption"));
            seed.Add(seedText);
            var field = new TextField { value = m.Seed, maxLength = Core2Seed.MaxLength, name = "create-seed" };
            Core2Ui.Classes(field, "c2-seed-field");
            field.RegisterValueChangedCallback(e => m.Seed = e.newValue);
            seed.Add(field);
            body.Add(seed);
            var destination = Core2Ui.Div("c2-row c2-sheet");
            var destinationText = Core2Ui.Div("c2-col c2-fill");
            destinationText.Add(Core2Ui.Text(m.DestinationLabel, "c2-strong"));
            destinationText.Add(Core2Ui.Text(m.DestinationHint, "c2-caption"));
            destination.Add(destinationText);
            destination.Add(Core2Ui.Text(review.Destination, ""));
            body.Add(destination);
            var split = Core2Ui.Div("c2-split");
            var character = Core2Ui.Div("c2-pane c2-sheet");
            character.Add(Core2Ui.Text(m.Classes.First(c => c.Id == m.Selected).Name.ToUpperInvariant(), "c2-micro"));
            character.Add(Core2Ui.Text("Forsaken", "c2-title-s"));
            var attributes = Core2Ui.Div("c2-row c2-wrap");
            foreach (var (key, value) in review.Attributes) { attributes.Add(Core2Ui.Text(key, "c2-stat-key")); attributes.Add(Core2Ui.Text(value.ToString(), "c2-stat-value")); }
            character.Add(attributes);
            var pools = Core2Ui.Div("c2-row c2-wrap");
            foreach (var (key, value) in review.Pools) { pools.Add(Core2Ui.Text(key, "c2-stat-key")); pools.Add(Core2Ui.Text(value, "c2-stat-value")); }
            character.Add(pools);
            split.Add(character);
            var gear = Core2Ui.Div("c2-pane c2-col");
            foreach (var (label, name) in review.Gear)
            {
                var sheet = Core2Ui.Div("c2-sheet");
                sheet.Add(Core2Ui.Text(label, "c2-micro"));
                sheet.Add(Core2Ui.Text(name, "c2-tile-title"));
                gear.Add(sheet);
            }
            split.Add(gear);
            body.Add(split);
            body.Add(Core2Ui.Text(Core2Text.T("creation.review.starterDeck").ToUpperInvariant(), "c2-micro"));
            body.Add(Core2Ui.Text(string.Join(" · ", review.Deck), "c2-text"));
        }

        private void Begin()
        {
            var error = _model.Begin();
            if (error != null) { Show(); return; }
            _begun?.Invoke();
        }

        /// <summary>JS title.slots door (new game): every slot holds a climb, so the player picks the one to overwrite.</summary>
        private void Slots(VisualElement screen)
        {
            var door = Core2Ui.Door("", Core2Text.T("title.slots.door.new"));
            foreach (var slot in _flow.Slots.List())
            {
                var index = slot.Slot;
                var cls = slot.ClassId != null ? _flow.Content.Classes.FirstOrDefault(c => c.Id == slot.ClassId)?.Name : null;
                var line = slot.State == Core2SlotState.Refused ? slot.Refusal : cls != null ? cls + " · Act " + slot.Act + " · Floor " + slot.Floor : "";
                var tile = Core2Ui.Tile(null, Core2Text.T("creation.review.slot", new { slot = index + 1 }), new[] { line });
                tile.Add(Core2Ui.Btn("slot-pick-" + index, Core2Text.T("title.slots.primary.new"), () =>
                {
                    _pickSlot = false;
                    _model = new Core2CreationModel(_flow, index, unchecked((uint)DateTime.UtcNow.Ticks));
                    Show();
                }, "c2-btn-small"));
                door.Body.Add(tile);
            }
            door.Body.Add(Core2Ui.Text(Core2Text.T("title.slots.start.occupied.prompt"), "c2-caption"));
            door.Foot.Add(Core2Ui.Btn("slot-back", Core2Text.T("common.back"), () => _back?.Invoke(), "c2-btn-fill"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }
    }
}
