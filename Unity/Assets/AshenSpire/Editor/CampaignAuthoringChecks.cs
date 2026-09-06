// CampaignAuthoringChecks.cs — executable Editor regression checks, not runtime code.
// MENU: AshenSpire/Run Authoring Checks. BATCH: -executeMethod ...Run.
// Uses owned transient drafts and scratch recovery JSON; authoritative content is read-only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AshenSpire.Domain;
using UnityEditor;
using UnityEngine;

namespace AshenSpire.Editor
{
    public static class CampaignAuthoringChecks
    {
        [Serializable] private sealed class Report { public int Passed; public string SourceHash; public List<string> Checks = new List<string>(); }
        [MenuItem("AshenSpire/Run Authoring Checks")]
        public static void Run()
        {
            var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
            var source = Path.Combine(root, "GameContent/Unity/campaign.json");
            var json = File.ReadAllText(source); var hash = CampaignContentFile.Hash(source);
            var report = new Report { SourceHash = hash };
            void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("Authoring check failed: " + name); report.Passed++; report.Checks.Add(name); Debug.Log("AUTHORING PASS: " + name); }
            var draft = ScriptableObject.CreateInstance<CampaignContentDraft>(); draft.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                draft.Content = JsonUtility.FromJson<CampaignDefinition>(json);
                using (var serialized = new SerializedObject(draft))
                {
                    foreach (var table in CampaignContentDraft.Tables)
                    {
                        var property = serialized.FindProperty(CampaignContentDraft.PropertyPath(table));
                        Check(property != null && property.isArray && property.arraySize == draft.Rows(table).Length, table + " has a structured serialized table binding");
                    }
                    var amount = serialized.FindProperty("Content.Cards").GetArrayElementAtIndex(0).FindPropertyRelative("Effects").GetArrayElementAtIndex(0).FindPropertyRelative("Amount");
                    var original = amount.intValue; var oldVolume = draft.Content.Audio.Volume;
                    Undo.IncrementCurrentGroup();
                    amount.intValue = original + 7; serialized.FindProperty("Content.Audio.Volume").floatValue = .12f;
                    Check(serialized.ApplyModifiedProperties(), "structured nested fields apply to the draft"); Undo.FlushUndoRecordObjects();
                    Check(draft.Content.Cards[0].Effects[0].Amount == original + 7 && Mathf.Approximately(draft.Content.Audio.Volume, .12f), "effect and sound settings share the same draft");
                    Undo.PerformUndo();
                    Check(draft.Content.Cards[0].Effects[0].Amount == original && Mathf.Approximately(draft.Content.Audio.Volume, oldVolume), "Undo restores nested fields and settings");
                    Undo.PerformRedo();
                    Check(draft.Content.Cards[0].Effects[0].Amount == original + 7, "Redo restores structured edits");
                    Undo.ClearUndo(draft);
                }
                draft.Content = JsonUtility.FromJson<CampaignDefinition>(json);
                var count = draft.Content.Cards.Length;
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(draft, "Duplicate card");
                var index = draft.Add("Cards", 0); Undo.FlushUndoRecordObjects();
                Check(draft.Content.Cards[index].Id == draft.Content.Cards[0].Id + "Copy", "duplicate has a readable unique ID");
                Undo.PerformUndo(); Check(draft.Content.Cards.Length == count, "Undo removes duplicate");
                Undo.PerformRedo(); Check(draft.Content.Cards.Length == count + 1, "Redo restores duplicate");
                var originalDamage = draft.Content.Cards[0].Effects[0].Amount;
                draft.Content.Cards[index].Effects[0].Amount += 11;
                Check(draft.Content.Cards[0].Effects[0].Amount == originalDamage, "duplicates do not share nested effect objects");
                var second = draft.Add("Cards", 0);
                Check(draft.Content.Cards[second].Id == draft.Content.Cards[0].Id + "Copy2", "repeated duplicate selects the next unused ID");
                Undo.ClearUndo(draft); Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(draft, "Remove card");
                draft.Remove("Cards", second); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(draft.Content.Cards.Length == count + 2, "Undo restores a removed row");
                Undo.ClearUndo(draft);
                var feedbackCount = draft.Content.Feedback.Cues.Length; draft.Add("FeedbackCues", 0);
                Check(draft.Content.Feedback.Cues.Length == feedbackCount + 1, "feedback cues support the same record operations");
                draft.Content = JsonUtility.FromJson<CampaignDefinition>(json);
                var cardIndex = Array.FindIndex(draft.Content.Cards, card => card.Id == "bloodrush");
                var rewardIndex = draft.Add("Cards", cardIndex); var rewardId = draft.Content.Cards[rewardIndex].Id;
                draft.AddReward(rewardId); draft.AddReward(rewardId); draft.Content.Validate();
                Check(draft.Content.RewardCards.Count(id => id == rewardId) == 1, "reward enrollment is unique and remains valid for copied class cards");
                var checkpoint = new CampaignDraftCheckpoint { DraftJson = JsonUtility.ToJson(draft.Content), SavedJson = JsonUtility.ToJson(JsonUtility.FromJson<CampaignDefinition>(json)), SourceHash = hash };
                var recovered = JsonUtility.FromJson<CampaignDraftCheckpoint>(JsonUtility.ToJson(checkpoint));
                var restored = JsonUtility.FromJson<CampaignDefinition>(recovered.DraftJson);
                Check(restored.Cards.Any(card => card.Id == rewardId) && restored.RewardCards.Contains(rewardId) && recovered.SourceHash == hash, "Unity serialization preserves unsaved draft and disk baseline through recovery");
                Check(CampaignContentFile.Hash(source) == hash, "Editor checks preserve authoritative source bytes");
                Directory.CreateDirectory(Path.Combine(root, "Builds"));
                File.WriteAllText(Path.Combine(root, "Builds/authoring-editor-checks.json"), JsonUtility.ToJson(report, true));
                Debug.Log("ASHENSPIRE_AUTHORING_CHECKS_PASSED " + report.Passed);
            }
            finally { Undo.ClearUndo(draft); UnityEngine.Object.DestroyImmediate(draft); }
        }
    }
}
