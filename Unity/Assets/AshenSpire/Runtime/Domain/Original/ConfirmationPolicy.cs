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

    public sealed class ConfirmationPolicy
    {
        /// <summary>Resources.Load path of the imported copy.</summary>
        public const string ResourcePath = "Original/confirmation-policies";
        public const string RemoveCard = "action.removeCard";
        public const string OverwriteSave = "action.overwriteSave";
        public const string DeleteSave = "action.deleteSave";

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
