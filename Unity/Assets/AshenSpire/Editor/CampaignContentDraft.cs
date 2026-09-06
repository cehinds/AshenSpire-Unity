// CampaignContentDraft.cs — editor-only Undo target; never saved as a game asset.
// CampaignContentWindow owns this object and checkpoints its JSON outside Assets.
// Keep disk baseline/hash on the window, outside Undo, so Undo after Save remains usable.
using System;
using System.Linq;
using AshenSpire.Domain;
using UnityEngine;

namespace AshenSpire.Editor
{
    public sealed class CampaignContentDraft : ScriptableObject
    {
        public CampaignDefinition Content;
        public static readonly string[] Tables = { "Cards", "Heroes", "Foes", "Encounters", "Equipment", "Tags", "FeedbackCues" };
        public static string PropertyPath(string table) => table == "FeedbackCues" ? "Content.Feedback.Cues" : "Content." + table;
        public Array Rows(string table)
        {
            if (table == "FeedbackCues") return Content.Feedback?.Cues ?? Array.Empty<FeedbackCue>();
            if (!Tables.Contains(table)) throw new ArgumentException("Unknown table: " + table);
            var field = typeof(CampaignDefinition).GetField(table);
            return (Array)field.GetValue(Content) ?? Array.CreateInstance(field.FieldType.GetElementType(), 0);
        }
        private void SetRows(string table, Array rows)
        {
            if (table == "FeedbackCues")
            {
                if (Content.Feedback == null) Content.Feedback = new FeedbackDefinition();
                Content.Feedback.Cues = (FeedbackCue[])rows;
            }
            else typeof(CampaignDefinition).GetField(table).SetValue(Content, rows);
        }
        public int Add(string table, int duplicate = -1)
        {
            var rows = Rows(table); var type = rows.GetType().GetElementType();
            var value = duplicate >= 0 ? JsonUtility.FromJson(JsonUtility.ToJson(rows.GetValue(duplicate)), type) : Activator.CreateInstance(type);
            var idField = type.GetField("Id");
            var prefix = duplicate >= 0 ? (string)idField.GetValue(value) + "Copy" : "new" + type.Name.Replace("Definition", "").Replace("FeedbackCue", "Cue");
            var ids = rows.Cast<object>().Select(row => (string)idField.GetValue(row)).ToArray();
            var id = prefix; var number = 2;
            while (ids.Contains(id)) id = prefix + number++;
            idField.SetValue(value, id);
            if (duplicate < 0)
            {
                type.GetField("Name")?.SetValue(value, "New " + type.Name.Replace("Definition", ""));
                foreach (var field in type.GetFields().Where(field => field.FieldType.IsArray))
                    field.SetValue(value, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }
            var expanded = Array.CreateInstance(type, rows.Length + 1);
            Array.Copy(rows, expanded, rows.Length); expanded.SetValue(value, rows.Length);
            SetRows(table, expanded); return rows.Length;
        }
        public void Remove(string table, int index)
        {
            var rows = Rows(table); var trimmed = Array.CreateInstance(rows.GetType().GetElementType(), rows.Length - 1);
            for (int from = 0, to = 0; from < rows.Length; from++) if (from != index) trimmed.SetValue(rows.GetValue(from), to++);
            SetRows(table, trimmed);
        }
        public void AddReward(string id)
        {
            if (!Content.RewardCards.Contains(id)) Content.RewardCards = Content.RewardCards.Concat(new[] { id }).ToArray();
        }
    }
}
