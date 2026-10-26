// Core2ProfileSnapshot.cs — what a Core 2 run and each of its fights record about the rules they run
// (SPEC.md §0.1: "each new run snapshots its resolved profile and a hash of the rules it runs, and each new
// fight stamps core2ActionRulesVersion: 1"; a save whose snapshot does not match the build refuses to load,
// named, without overwriting it).
//
// JS at the pin: src/model/state.js createRunState keeps run.gameProfile (the resolved profile, hash included);
// src/model/gameProfile.js resolveGameProfile(snapshot) re-validates it (validateGameProfileSnapshot);
// src/engine/combat.js createCombat stamps combat.fixCoreRules (snapshotFixCoreRules) and
// combat.core2ActionRulesVersion = CORE2_ACTION_RULES_VERSION; src/model/core2ActionPolicy.js
// core2ActionPolicyEnabled reads that stamp. Unity keeps the identity (ids, hashes, versions), not a second copy of
// the profile: the export it names is a byte-identity artifact (Core2Content verifies every sha256).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A save made under other rules: it is refused by name and the stored bytes are left alone (SPEC §0.1).</summary>
    public sealed class Core2ProfileMismatchException : IOException
    {
        public IReadOnlyList<string> Differences { get; }
        public Core2ProfileMismatchException(IReadOnlyList<string> differences)
            : base("This run was made under other Core 2 rules and can't be continued by this build: " + string.Join("; ", differences))
        { Differences = differences; }
    }

    /// <summary>The run-level snapshot: which resolved profile, rules hash and JS pin a Core 2 run was started under.</summary>
    public sealed class Core2ProfileSnapshot
    {
        public const int SnapshotVersion = 1;
        /// <summary>JS combatExpansionVersion for every new run (src/model/state.js).</summary>
        public const int CombatExpansionVersion = 2;

        public string ProfileId { get; }
        public string Core { get; }
        /// <summary>The resolved profile hash (JS profile.hash, "sha256:…").</summary>
        public string RulesHash { get; }
        public string RuntimeHash { get; }
        public string JsCommit { get; }
        public string JsBuild { get; }
        /// <summary>JS CORE2_ACTION_RULES_VERSION this build stamps on new fights.</summary>
        public int Core2ActionRulesVersion { get; }
        public int FixCoreVersion { get; }

        public Core2ProfileSnapshot(string profileId, string core, string rulesHash, string runtimeHash, string jsCommit, string jsBuild, int core2ActionRulesVersion, int fixCoreVersion)
        {
            ProfileId = profileId; Core = core; RulesHash = rulesHash; RuntimeHash = runtimeHash; JsCommit = jsCommit; JsBuild = jsBuild;
            Core2ActionRulesVersion = core2ActionRulesVersion; FixCoreVersion = fixCoreVersion;
            if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(core) || string.IsNullOrEmpty(rulesHash))
                throw new InvalidDataException("Core 2 profile snapshot needs a profile id, core and rules hash");
        }

        /// <summary>The snapshot a new run takes of the loaded export.</summary>
        public static Core2ProfileSnapshot Take(Core2Content content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var m = content.Manifest;
            return new Core2ProfileSnapshot(m.ProfileId, m.Core, m.ProfileHash, m.RuntimeHash, m.JsCommit, m.JsBuild, m.Core2ActionRulesVersion,
                (int?)content.Profile["modules"]?["effects"]?["fixCoreVersion"] ?? 0);
        }

        /// <summary>Refuses (named) when this snapshot was taken under rules other than the loaded export's.</summary>
        public void RequireMatches(Core2Content content)
        {
            var now = Take(content);
            var differences = new List<string>();
            void Same(string label, object saved, object current) { if (!Equals(saved, current)) differences.Add(label + " " + saved + " (this build: " + current + ")"); }
            Same("core", Core, now.Core);
            Same("profile", ProfileId, now.ProfileId);
            Same("rules hash", RulesHash, now.RulesHash);
            Same("action rules version", Core2ActionRulesVersion, now.Core2ActionRulesVersion);
            Same("FixCore version", FixCoreVersion, now.FixCoreVersion);
            if (differences.Count > 0) throw new Core2ProfileMismatchException(differences);
        }

        public bool Matches(Core2Content content)
        {
            try { RequireMatches(content); return true; }
            catch (Core2ProfileMismatchException) { return false; }
        }

        public JObject ToJson() => new JObject
        {
            ["snapshotVersion"] = SnapshotVersion,
            ["profileId"] = ProfileId,
            ["core"] = Core,
            ["rulesHash"] = RulesHash,
            ["runtimeHash"] = RuntimeHash,
            ["jsCommit"] = JsCommit,
            ["jsBuild"] = JsBuild,
            ["core2ActionRulesVersion"] = Core2ActionRulesVersion,
            ["fixCoreVersion"] = FixCoreVersion,
            ["combatExpansionVersion"] = CombatExpansionVersion,
        };

        public static Core2ProfileSnapshot FromJson(JObject row)
        {
            if (row == null) throw new InvalidDataException("Core 2 profile snapshot is missing");
            if ((int?)row["snapshotVersion"] != SnapshotVersion) throw new InvalidDataException("Core 2 profile snapshot version " + row["snapshotVersion"] + " is not supported");
            if ((int?)row["combatExpansionVersion"] != CombatExpansionVersion) throw new InvalidDataException("Core 2 profile snapshot must record combatExpansionVersion " + CombatExpansionVersion);
            return new Core2ProfileSnapshot((string)row["profileId"], (string)row["core"], (string)row["rulesHash"], (string)row["runtimeHash"],
                (string)row["jsCommit"], (string)row["jsBuild"], (int?)row["core2ActionRulesVersion"] ?? 0, (int?)row["fixCoreVersion"] ?? 0);
        }
    }

    /// <summary>
    /// The per-fight stamp (JS combat.core2ActionRulesVersion and combat.fixCoreRules). A fight keeps the version it
    /// started with: pricing reads the stamp, so a fight saved without it keeps its accepted pricing (JS core2ActionPolicy.js).
    /// </summary>
    public sealed class Core2FightStamp
    {
        public int Core2ActionRulesVersion { get; }
        public Core2Rules Rules { get; }

        public Core2FightStamp(Core2Rules rules, int core2ActionRulesVersion)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (core2ActionRulesVersion != 0 && core2ActionRulesVersion != Core2Combat.ActionRulesVersion)
                throw new InvalidDataException("core2ActionRulesVersion must be " + Core2Combat.ActionRulesVersion + " when present (JS combatSnapshot.js)");
            Core2ActionRulesVersion = core2ActionRulesVersion;
        }

        /// <summary>A new fight of a run: FixCore on stamps the current action rules version (JS combat.js createCombat).</summary>
        public static Core2FightStamp NewFight(Core2ProfileSnapshot run, Core2Rules rules)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.Core2ActionRulesVersion != Core2Combat.ActionRulesVersion)
                throw new Core2ProfileMismatchException(new[] { "action rules version " + run.Core2ActionRulesVersion + " (this build: " + Core2Combat.ActionRulesVersion + ")" });
            return new Core2FightStamp(rules, rules.FixCoreEnabled ? Core2Combat.ActionRulesVersion : 0);
        }

        /// <summary>JS core2ActionPolicyEnabled: the fight carries version 1 and FixCore is on.</summary>
        public bool ActionPolicyEnabled => Core2ActionRulesVersion == Core2Combat.ActionRulesVersion && Rules.FixCoreEnabled;
    }
}
