// OriginalGameSession.cs — composes native run rooms and native combat commands.
// One instance owns the live game. The UI observes copies and invokes commands;
// a failed command restores its full pre-command state, including every RNG stream.
// Persist Snapshot after Changed. Frozen authored rules are restored from the save.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalGameSession
    {
        private OriginalRunSession _run;
        private CombatSession _combat;
        private OriginalContentCatalog _catalog;
        private JObject _mechanics;
        private IOriginalRunContent _callbacks;
        private WeaponCardProjection _projection;
        private AttributeProgression _progression;
        private JObject _profileOverrides, _projectionPlayer;
        public event Action Changed;
        public OriginalRunPhase Phase => _run.Phase;
        public OriginalContentCatalog Catalog => _catalog;
        public JObject Player => _combat?.Player ?? _run.Player();
        public JObject RunPlayer => _run.Player();
        public JObject Room => _run.Room();
        public JObject Map => _run.Map();
        public int ActNumber => _run.ActNumber;
        public string[] LegalNodeIds => _run.LegalNodeIds();
        public JArray Hand => _combat?.Hand ?? new JArray();
        public JArray Enemies => _combat?.Enemies ?? new JArray();
        public int Turn => _combat?.Turn ?? 0;
        public JArray EventChoices => _run.EventChoices();
        public JArray DraftChoices => _run.DraftChoices();
        private JArray _lastEvents = new JArray();
        public JArray LastEvents { get => (JArray)_lastEvents.DeepClone(); private set => _lastEvents = (JArray)value.DeepClone(); }
        private OriginalGameSession() { }
        public static OriginalGameSession StartConfigured(OriginalContentCatalog catalog, JObject supplement, JObject mechanics, JObject player, uint seed, JObject options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Properties().Any(p => !new[] { "custom", "keepsakeId", "customization" }.Contains(p.Name))) throw new ArgumentException("Unknown run setup option.");
            var configured = (JObject)player.DeepClone();
            if (options["custom"] != null && !(options["custom"] is JObject)) throw new ArgumentException("Custom rules must be an object.");
            configured["custom"] = OriginalCustomRunRules.Normalize(options["custom"] as JObject);
            foreach (var key in new[] { "keepsakeId", "customization" }) if (options[key] != null) configured[key] = options[key].DeepClone();
            return Start(catalog,supplement,mechanics,configured,seed);
        }
        public static OriginalGameSession Start(OriginalContentCatalog catalog, JObject supplement, JObject mechanics, JObject player, uint seed)
        {
            var session = new OriginalGameSession { _catalog = catalog, _mechanics = (JObject)mechanics.DeepClone() };
            var stats = new OriginalPlayerProjection(catalog, mechanics);
            session._callbacks = new OriginalRunContent(catalog, reconcile: stats.Reconcile);
            player = (JObject)player.DeepClone(); stats.Reconcile(player);
            var frozen = (JObject)supplement.DeepClone(); frozen["mechanics"] = mechanics.DeepClone();
            session._run = OriginalRunSession.Start(catalog, frozen, player, seed, session._callbacks);
            session.InitializeProjection();
            return session;
        }
        public static OriginalGameSession Restore(JObject snapshot)
        { var session = new OriginalGameSession(); session.RestoreState(snapshot); return session; }
        private void RestoreState(JObject snapshot)
        {
            if ((int?)snapshot?["schemaVersion"] != 1 || !(snapshot["content"] is JObject) || !(snapshot["supplement"]?["mechanics"] is JObject) || !(snapshot["run"]?["progression"] is JObject))
                throw new ArgumentException("Native save is missing its frozen content or rule configuration.");
            _catalog = new OriginalContentCatalog(snapshot["content"].ToString());
            snapshot = (JObject)snapshot.DeepClone();
            // Early native v1 saves predate kit identity. Only the complete absence
            // of both fields admits the explicit baseline migration; partial IDs fail.
            new OriginalStartingOptions(_catalog).ValidateSaved((JObject)snapshot["run"], snapshot["run"]["profileMeta"] as JObject, legacy: true);
            _mechanics = (JObject)snapshot["supplement"]["mechanics"].DeepClone();
            _callbacks = new OriginalRunContent(_catalog, reconcile: new OriginalPlayerProjection(_catalog, _mechanics).Reconcile);
            _run = OriginalRunSession.Restore(snapshot, _callbacks); _combat = null; InitializeProjection(); EnsureCombat();
        }
        private void InitializeProjection()
        {
            _projectionPlayer = _run.Player();
            _progression = new AttributeProgression((JObject)_projectionPlayer["progression"]);
            _profileOverrides = _progression.BaselineProfiles(_catalog);
            _projection = new WeaponCardProjection(_catalog);
        }
        public JObject Snapshot() => _run.Snapshot();
        public JObject Resolve(JObject instance)
        {
            var run = _projectionPlayer;
            var projection = _projection.Resolve(instance, (JObject)run["loadout"], (string)run["classId"], (JObject)run["attributes"], _profileOverrides);
            return (JObject)_progression.ResolveCard(projection, (JObject)run["attributes"], _catalog)["card"];
        }
        public JObject Cost(JObject instance) => _combat != null ? _combat.CardCost(instance) : CardMechanics.CostProfile(Resolve(instance));
        private void EnsureCombat()
        {
            if (_run.Phase != OriginalRunPhase.Combat) { _combat = null; return; }
            if (_combat != null) return;
            if (_run.Room()["combatSnapshot"] is JObject saved) _combat = CombatSession.Restore(_catalog, _mechanics, saved, Resolve);
            else
            {
                var player = _run.Player(); player["energyMax"] = player["energy"].DeepClone(); player["drawPerTurn"] = player["draw"].DeepClone();
                var encounter = _catalog.Record("encounters", (string)_run.Room()["encounterId"]);
                var custom = new OriginalCustomRunRules(_catalog.Data()).CombatOptions(player,(string)encounter["pool"]);
                player["startStatuses"] = new JArray(((JArray)custom["playerStatuses"]).Concat(player["startStatuses"] as JArray ?? new JArray()).Select(row => row.DeepClone()));
                _combat = new CombatSession(_catalog, _mechanics, _run.CreateRandom(), player, ((JArray)player["deck"]).OfType<JObject>(), encounter["enemies"].Values<string>(), Resolve,(double)custom["hpMult"],(JArray)custom["enemyStatuses"]);
                CommitCombat();
            }
        }
        private void CommitCombat()
        {
            var saved = _combat.Snapshot(); var streams = new RandomStreams((uint)saved["seed"], ((JObject)saved["rng"]).Properties().ToDictionary(p => p.Name, p => (uint)p.Value));
            if (_combat.Result != null)
            {
                var player = _combat.Player;
                player["damageDealt"] = saved["events"].Where(x => (string)x["type"] == "damageDealt" && (string)x["sourceId"] == "player").Sum(x => (int)x["amount"]);
                player["damageTaken"] = saved["events"].Where(x => (string)x["type"] == "hpLost" && (string)x["targetId"] == "player").Sum(x => (int)x["amount"]);
                _run.CompleteCombat(_combat.Result, player, streams); _combat = null;
            }
            else _run.SaveCombat(saved, streams);
        }
        private void Change(Action command)
        {
            var before = Snapshot(); var previousEvents = LastEvents;
            try { LastEvents = new JArray(); command(); _projectionPlayer = _run.Player(); EnsureCombat(); }
            catch { RestoreState(before); LastEvents = previousEvents; throw; }
            Changed?.Invoke();
        }
        public void Enter(string id) => Change(() => { if (!_run.EnterNode(id)) throw new ArgumentException("Choose a connected route."); });
        public void PickDraft(string cardId) => Change(() => { if (!_run.PickDraft(cardId)) throw new ArgumentException("Choose one of the current draft offers."); });
        public void Play(string instance, string target) => Change(() => { if (_combat == null) throw new InvalidOperationException("No active fight."); LastEvents = _combat.PlayCard(instance, target); CommitCombat(); });
        public void EndTurn() => Change(() => { if (_combat == null) throw new InvalidOperationException("No active fight."); LastEvents = _combat.EndTurn(); CommitCombat(); });
        public void CatchBreath() => Change(() => { if (_combat == null) throw new InvalidOperationException("No active fight."); LastEvents = _combat.CatchBreath(); CommitCombat(); });
        public void DrinkCharge(string kind) => Change(() => { if (_combat == null) throw new InvalidOperationException("No active fight."); LastEvents = _combat.DrinkCharge(kind); CommitCombat(); });
        public void DrinkFlask(int slot, string target = null) => Change(() => { if (_combat == null) throw new InvalidOperationException("No active fight."); LastEvents = _combat.DrinkFlask(slot, target); CommitCombat(); });
        public void Service(string service, JObject request) => Change(() => { if (!_run.UseService(service, request)) throw new ArgumentException("This service is unavailable here or its requirements are not met."); });
        public void Reward(string kind, string id = null) => Change(() => { if (!_run.CollectReward(kind, id)) throw new ArgumentException("That reward cannot be collected."); });
        public void ContinueRewards() => Change(() => _run.ContinueRewards(false));
        public void ChooseEvent(string id) => Change(() => { if (!_run.ChooseEvent(id)) throw new ArgumentException("This choice's requirements are not met."); });
        public void LeaveEvent() => Change(() => _run.LeaveEvent());
        public void Rest() => Change(() => _run.Rest());
        public void Buy(string kind, int index) => Change(() => { if (!_run.BuyShopItem(kind, index)) throw new ArgumentException("This purchase is unavailable or too expensive."); });
        public void LeaveShop() => Change(() => _run.LeaveShop());
        public void LeaveShrine() => Change(() => _run.LeaveShrine());
        public int OpenedSets(string slot) => _combat == null ? _run.OpenedSets(slot) : new OriginalCombatEquipment(_catalog, _mechanics).OpenedSets(_run.Player(), slot);
        public JObject SwapPrice(string slot, int index)
        {
            if (_combat == null) throw new InvalidOperationException("No active fight.");
            return new OriginalCombatEquipment(_catalog, _mechanics).Price(_run.Player(), _combat.Snapshot(), slot, index);
        }
        public int SwapsLeft => _combat == null ? 0 : new OriginalCombatEquipment(_catalog, _mechanics).SwapsLeft(_combat.Snapshot());
        public void SwapSet(string slot, int index) => Change(() =>
        {
            if (_combat == null) throw new InvalidOperationException("No active fight.");
            var change = new OriginalCombatEquipment(_catalog, _mechanics).Apply(_run.Player(), _combat.Snapshot(), slot, index);
            var snapshot = _run.Snapshot(); snapshot["run"] = change["run"].DeepClone();
            snapshot["run"]["room"]["combatSnapshot"] = change["combat"].DeepClone();
            // The complete draft restores together so card validation reads the new loadout.
            RestoreState(snapshot);
            LastEvents = _combat.FinishEquipmentSwap((JArray)change["events"], (bool)change["endsTurn"]);
            CommitCombat();
        });
        public void Equip(string slot, int setIndex, string itemId) => Change(() => { if (!_run.Equip(slot, setIndex, itemId)) throw new ArgumentException("This equipment change is unavailable or its requirements are not met."); });
        public void SelectSet(string slot, int index)
        {
            if (_combat != null) { SwapSet(slot, index); return; }
            Change(() => { if (!_run.SelectSet(slot, index)) throw new ArgumentException("That set is locked or cannot be selected here."); });
        }
    }
}
