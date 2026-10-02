// ConfirmationPolicy.cs — which confirmation an action owes (US-13.3).
// DATA: GameContent/Unity/Original/confirmation-policies.json, a verbatim copy of the
// original game's content/framework/confirmationPolicies.json (imported into
// Resources/Original by BuildTools.ImportContent; never edit the Resources copy).
// RULE (original framework contract): every action binds to exactly one policy, and an
// action flagged destructive binds to a DESTRUCTIVE policy. Which actions need a hold is
// a characteristic of the action in data, never a list of call sites.
// Engine-independent: C# 9 / netstandard2.1, no UnityEngine.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public enum ConfirmationLevel { None, Reversible, Commitment, Destructive }

    /// <summary>One place a confirmation action is (or deliberately is not) presented in Unity.</summary>
    public sealed class ConfirmationRoute
    {
        public ConfirmationRoute(string actionId, string constant, string controls, string source, string unrouted = null)
        { ActionId = actionId; Constant = constant; Controls = controls; Source = source; Unrouted = unrouted; }
        public static ConfirmationRoute None(string actionId, string reason) => new ConfirmationRoute(actionId, null, null, null, reason);
        public string ActionId { get; }
        /// <summary>The ConfirmationPolicy constant the source passes to HoldConfirmButton.</summary>
        public string Constant { get; }
        public string Controls { get; }
        /// <summary>Path under Unity/Assets/AshenSpire/Runtime, or null when unrouted.</summary>
        public string Source { get; }
        public string Unrouted { get; }
        public bool Routed => Source != null;
    }

    public sealed class ConfirmationPolicy
    {
        /// <summary>Resources.Load path of the imported copy.</summary>
        public const string ResourcePath = "Original/confirmation-policies";
        public const string RemoveCard = "action.removeCard";
        public const string OverwriteSave = "action.overwriteSave";
        public const string DeleteSave = "action.deleteSave";
        public const string LoadSlot = "action.loadSlot";
        public const string QuitWithoutSaving = "action.quitWithoutSaving";
        public const string AbandonRun = "action.abandonRun";

        /// <summary>Where each DESTRUCTIVE action meets the Unity UI. UnityTests/HoldConfirm asserts this
        /// covers every DESTRUCTIVE action in the data, that each routed source binds its constant
        /// through HoldConfirmButton, and that an unrouted action states why.</summary>
        public static readonly IReadOnlyList<ConfirmationRoute> UnityRoutes = new[]
        {
            new ConfirmationRoute(RemoveCard, nameof(RemoveCard), "native-remove-<instanceId>", "Presentation/OriginalRunPanel.cs"),
            new ConfirmationRoute(RemoveCard, nameof(RemoveCard), "coop-remove-<instanceId>", "Presentation/OriginalCoopPanel.cs"),
            new ConfirmationRoute(OverwriteSave, nameof(OverwriteSave), "native-slot-confirm (after native-slot-<n>-new)", "Presentation/OriginalSlotPanel.cs"),
            new ConfirmationRoute(DeleteSave, nameof(DeleteSave), "native-slot-confirm (after native-slot-<n>-delete)", "Presentation/OriginalSlotPanel.cs"),
            new ConfirmationRoute(LoadSlot, nameof(LoadSlot), "native-slot-<n>-continue while the climb in memory has unsaved progress", "Presentation/OriginalSlotPanel.cs"),
            new ConfirmationRoute(LoadSlot, nameof(LoadSlot), "title native-continue while the climb in memory has unsaved progress", "Presentation/OriginalTitlePanel.cs"),
            ConfirmationRoute.None(QuitWithoutSaving, "Unity has no quit-without-saving control: native-menu always saves before returning to title, and a failed save keeps the climb in memory (RunController.Menu/SaveOriginalSlot)."),
            ConfirmationRoute.None(AbandonRun, "Unity has no abandon-run control; a climb ends only in victory or defeat, or by deleting/overwriting its slot (routed above)."),
        };

        private readonly Dictionary<string, ConfirmationLevel> _policies = new Dictionary<string, ConfirmationLevel>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _actions = new Dictionary<string, string>(StringComparer.Ordinal);

        public ConfirmationPolicy(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("confirmation-policies.json is empty.");
            var root = JObject.Parse(json);
            foreach (var row in (root["policies"] as JArray ?? throw new InvalidDataException("confirmation-policies.json requires policies.")).OfType<JObject>())
            {
                var id = (string)row["id"];
                if (string.IsNullOrEmpty(id) || _policies.ContainsKey(id)) throw new InvalidDataException("Missing or duplicate confirmation policy id: " + id);
                _policies[id] = ParseLevel((string)row["level"], id);
            }
            foreach (var row in (root["actions"] as JArray ?? throw new InvalidDataException("confirmation-policies.json requires actions.")).OfType<JObject>())
            {
                var id = (string)row["id"]; var policy = (string)row["policyId"];
                if (string.IsNullOrEmpty(id) || _actions.ContainsKey(id)) throw new InvalidDataException("Missing or duplicate confirmation action id: " + id);
                if (policy == null || !_policies.TryGetValue(policy, out var level)) throw new InvalidDataException("Action " + id + " binds to unknown policy " + policy);
                if (((bool?)row["destructive"] ?? false) != (level == ConfirmationLevel.Destructive))
                    throw new InvalidDataException("Action " + id + " destructive flag disagrees with policy " + policy);
                _actions[id] = policy;
            }
            if (_actions.Count == 0) throw new InvalidDataException("confirmation-policies.json declares no actions.");
        }

        public IReadOnlyCollection<string> Actions => _actions.Keys;
        public IReadOnlyList<string> DestructiveActions => _actions.Keys.Where(RequiresHold).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        public bool Knows(string actionId) => actionId != null && _actions.ContainsKey(actionId);
        public string PolicyOf(string actionId) => Knows(actionId) ? _actions[actionId] : throw new ArgumentException("Unknown confirmation action: " + actionId);

        /// <summary>The level an action owes. Unknown ids throw: an unruled action must not quietly skip its confirmation.</summary>
        public ConfirmationLevel Level(string actionId) => _policies[PolicyOf(actionId)];
        public bool RequiresHold(string actionId) => Level(actionId) == ConfirmationLevel.Destructive;

        private static ConfirmationLevel ParseLevel(string value, string id)
        {
            switch (value)
            {
                case "NONE": return ConfirmationLevel.None;
                case "REVERSIBLE": return ConfirmationLevel.Reversible;
                case "COMMITMENT": return ConfirmationLevel.Commitment;
                case "DESTRUCTIVE": return ConfirmationLevel.Destructive;
                default: throw new InvalidDataException("Unknown confirmation level '" + value + "' on " + id);
            }
        }
    }
}
