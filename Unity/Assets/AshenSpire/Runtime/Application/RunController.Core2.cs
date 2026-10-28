// RunController.Core2.cs — new runs are Core 2 runs (port plan phases 8–10 on screen). The title's Begin the climb,
// Quick start, Continue and Load now reach the Core 2 run host instead of the 0.5.5 native climb:
//   Begin the climb  → Core2CreationScreen (class, then Review with seed and slot) → Core2RunHost
//   Quick start      → the first open class on a fresh seed in the first empty slot → Core2RunHost
//   Continue         → the newest Core 2 save (Core2SaveSlots.Latest) → Core2RunHost on the screen it was saved at
//   Load             → the three Core 2 slots
// STORAGE: Core2SaveSlots over PlayerPrefs, per channel, under AshenSpire.Core2.* keys (compressed on WebGL like the
// native slots). Old 0.5.5 saves keep their keys and are never read here: as JS does with a save it cannot load, the
// title simply offers no Continue for them. A Core 2 save from other rules is refused by name on the title.
// SETTINGS: the Core 2 Deck settings (deckEditing, deckEditingWhere, deckMin/MaxSize, playInDeckOrder) are not in the
// Unity settings store yet; the run reads the JS defaults (content deckRules.defaults) until they are.
using System;
using System.Linq;
using AshenSpire.Domain.Core2;
using AshenSpire.Domain;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation;
using AshenSpire.Presentation.Core2;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private Core2RunData _core2Data;
        private Core2SaveSlots _core2Slots;
        private Core2RunFlow _core2Flow;
        private Core2RunHost _core2Host;
        private string _core2Channel = "desktop";

        /// <summary>PlayerPrefs (compressed on WebGL) as the Core 2 store.</summary>
        private sealed class Core2PrefsStorage : ICore2Storage
        {
            private readonly IOriginalSaveStorage _inner;
            public Core2PrefsStorage(IOriginalSaveStorage inner) { _inner = inner; }
            public string Get(string key) { var v = _inner.Read(key); return string.IsNullOrEmpty(v) ? null : v; }
            public void Set(string key, string value) => _inner.Write(key, value);
            public void Delete(string key) => _inner.Delete(key);
            public void Flush() => _inner.Flush();
        }

        private void InitCore2(string channel)
        {
            _core2Channel = channel;
            Core2Screens.Interrupted = () => _interruption != null && _interruption.IsInterrupted;
            _view.NativeRequested += NewCore2;
            _view.NativeContinueRequested += ContinueCore2;
            _view.NativeQuickStartRequested += QuickStartCore2;
            _view.SlotsRequested += ShowCore2Slots;
        }

        private void DisposeCore2()
        {
            if (_view != null)
            {
                _view.NativeRequested -= NewCore2;
                _view.NativeContinueRequested -= ContinueCore2;
                _view.NativeQuickStartRequested -= QuickStartCore2;
                _view.SlotsRequested -= ShowCore2Slots;
            }
            _core2Host?.Dispose();
            _core2Host = null;
        }

        /// <summary>The Core 2 slots and flow, built on first use (a broken export reports on the title, never crashes).</summary>
        private Core2RunFlow Core2Flow()
        {
            if (_core2Flow != null) return _core2Flow;
            _core2Data ??= new Core2RunData(Core2Resources.Load());
            IOriginalSaveStorage storage = new OriginalDelegateSaveStorage(key => PlayerPrefs.GetString(key, ""), (key, value) => PlayerPrefs.SetString(key, value), PlayerPrefs.Save, PlayerPrefs.DeleteKey);
#if UNITY_WEBGL && !UNITY_EDITOR
            storage = new OriginalCompressedSaveStorage(storage);
#endif
            _core2Slots = new Core2SaveSlots(new Core2PrefsStorage(storage), _core2Data.Content, _core2Channel);
            _core2Flow = new Core2RunFlow(_core2Data, _core2Slots);
            if (_core2Flow.ProfileRefusal != null) Debug.LogWarning("Core 2 profile not loaded; it will not be overwritten: " + _core2Flow.ProfileRefusal);
            return _core2Flow;
        }

        /// <summary>Whether the title's Continue has a Core 2 run to resume (0.5.5 saves never count).</summary>
        private bool HasCore2Save()
        {
            try { return Core2Flow().Slots.HasContinue; }
            catch (Exception error) { Debug.LogWarning("Core 2 saves unavailable: " + error.Message); return false; }
        }

        private void Core2Title(string notice)
        {
            _view.NativeSaveAvailable = HasCore2Save();
            _view.Title(_content, _saves.HasSave, notice);
            _view.PersistenceNotice(_profileNotice);
            MusicTitle();
        }

        private bool TryCore2(out Core2RunFlow flow)
        {
            flow = null;
            try { flow = Core2Flow(); return true; }
            catch (Exception error) { Debug.LogException(error); Core2Title("The Core 2 content could not be loaded: " + error.Message); return false; }
        }

        private static uint FreshSeed() => unchecked((uint)DateTime.UtcNow.Ticks ^ (uint)Environment.TickCount);

        // Title "Begin the climb": creation in the first empty slot; with every slot full, the slot step comes first.
        private void NewCore2()
        {
            if (!TryCore2(out var flow)) return;
            new Core2CreationScreen(_view.Router, _view.Root, flow, flow.Slots.FirstEmpty(), FreshSeed(), HostCore2, () => Core2Title(null)).Show();
            MusicTitle();
        }

        private void QuickStartCore2()
        {
            if (!TryCore2(out var flow)) return;
            var slot = flow.Slots.FirstEmpty();
            if (slot < 0) { NewCore2(); return; }
            var classId = flow.Content.Classes.Select(c => c.Id).First(flow.ClassAvailable);
            var error = flow.Begin(classId, Core2Seed.ToText(FreshSeed()), slot);
            if (error != null) { Core2Title(error); return; }
            HostCore2();
        }

        private void ContinueCore2() => LoadCore2(-1);

        private void LoadCore2(int slot)
        {
            if (!TryCore2(out var flow)) return;
            var error = flow.Continue(slot);
            if (error != null) { Core2Title(error); return; }
            HostCore2();
        }

        /// <summary>Title "Load": the three Core 2 slots (JS title.slots door, load).</summary>
        private void ShowCore2Slots()
        {
            if (!TryCore2(out var flow)) return;
            var body = _view.Router.Show("core2:load");
            Core2Ui.Style(_view.Root);
            ScreenRouter.SetFixed(body, true);
            var screen = Core2Ui.Div("c2-screen c2-fill");
            body.Add(screen);
            var door = Core2Ui.Door("", Core2Text.T("title.slots.door.load"));
            foreach (var info in flow.Slots.List())
            {
                var slot = info.Slot;
                var cls = info.ClassId != null ? flow.Content.Classes.FirstOrDefault(c => c.Id == info.ClassId)?.Name : null;
                var line = info.State == Core2SlotState.Refused ? info.Refusal : info.State == Core2SlotState.Empty ? "—" : cls + " · Act " + info.Act + " · Floor " + info.Floor;
                var tile = Core2Ui.Tile(null, Core2Text.T("creation.review.slot", new { slot = slot + 1 }), new[] { line }, info.State != Core2SlotState.Ready);
                tile.Add(Core2Ui.Btn("load-slot-" + slot, Core2Text.T("title.slots.primary.load"), () => LoadCore2(slot), "c2-btn-small", info.State == Core2SlotState.Ready));
                door.Body.Add(tile);
            }
            door.Foot.Add(Core2Ui.Btn("load-back", Core2Text.T("common.back"), () => Core2Title(null), "c2-btn-fill"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }

        private void HostCore2()
        {
            _core2Host?.Dispose();
            _core2Host = new Core2RunHost(_view.Router, _view.Root, _core2Flow, ExitCore2);
            _core2Host.Rendered += MusicCore2;
            _core2Host.Start();
        }

        private void ExitCore2(string notice)
        {
            _core2Host = null;
            Core2Title(notice);
        }

        private void MusicCore2()
        {
            if (_music == null || _core2Flow?.Session == null) return;
            var session = _core2Flow.Session;
            var phase = session.Screen switch
            {
                Core2Screen.Map => "Map", Core2Screen.Fight => "Combat", Core2Screen.Reward => "Rewards", Core2Screen.Shop => "Shop",
                Core2Screen.Rest => "Shrine", Core2Screen.Finished => session.Run.Finish?.Victory == true ? "Victory" : "Defeat", _ => "Event",
            };
            _music.Enter(MusicSceneMap.ForNativePhase(phase, session.Screen == Core2Screen.Fight ? (string)session.FightEncounter?["pool"] : null), session.Run.ActNumber);
        }
    }
}
