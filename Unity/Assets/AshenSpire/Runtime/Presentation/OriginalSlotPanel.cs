// OriginalSlotPanel.cs — the title's three save slots (HTML: saveSlotSelector.js, SPEC §3.12).
// WIRING: CampaignView.Slots supplies the slot list and callbacks; this view owns no save
// state and never touches storage. RunController.Slots performs load/new/copy/delete.
// FLOW: each slot row shows class, act/floor, seed and last-saved time with Continue,
// New (Overwrite when occupied), Copy (into the first empty slot) and Delete. Overwrite
// and Delete ask first; the question replaces the list until confirmed or cancelled.
// IDS (diagnostics/playtests): native-slot-<n>-continue|new|copy|delete, native-slot-confirm,
// native-slot-cancel, native-slots-back. <n> is the 0-based domain slot; labels are 1-based.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalSlotPanel
    {
        public delegate Button ControlFactory(string id, string label, Action clicked, string style);
        private readonly VisualElement _host;
        private readonly IReadOnlyList<OriginalSaveSlotInfo> _slots;
        private readonly Func<string, string> _className;
        private readonly Action<int> _load, _start, _delete;
        private readonly Action<int, int> _copy;
        private readonly Action _back, _report;
        private readonly ControlFactory _control;
        private readonly VisualElement _content = new VisualElement();
        public OriginalSlotPanel(VisualElement host, IReadOnlyList<OriginalSaveSlotInfo> slots, Func<string, string> className, string notice,
            Action<int> load, Action<int> start, Action<int> delete, Action<int, int> copy, Action back, ControlFactory control, Action report)
        {
            _host = host; _slots = slots ?? Array.Empty<OriginalSaveSlotInfo>(); _className = className ?? (id => id);
            _load = load; _start = start; _delete = delete; _copy = copy; _back = back; _control = control; _report = report;
            if (!string.IsNullOrEmpty(notice)) _host.Add(Text(notice, "notice"));
            _content.AddToClassList("original-slot-list"); _host.Add(_content);
            List();
        }
        private static bool Occupied(OriginalSaveSlotInfo slot) => slot.State != OriginalSaveSlotState.Empty;
        private static bool Loadable(OriginalSaveSlotInfo slot) => slot.State == OriginalSaveSlotState.Ready || slot.State == OriginalSaveSlotState.RecoveredBackup;
        private void List()
        {
            _content.Clear();
            var firstEmpty = _slots.FirstOrDefault(s => !Occupied(s));
            foreach (var slot in _slots)
            {
                var n = slot.Slot; var row = new VisualElement(); row.AddToClassList("panel"); row.AddToClassList("original-slot");
                row.Add(Text(Heading(slot), "node-title"));
                row.Add(Text(Facts(slot), "caption"));
                if (Loadable(slot)) row.Add(_control("native-slot-" + n + "-continue", "Continue", () => _load(n), "primary"));
                row.Add(Occupied(slot)
                    ? _control("native-slot-" + n + "-new", "New (overwrite)", () => Confirm("Overwrite slot " + (n + 1) + "?", "Starting here deletes this saved climb and begins a new one. There is no way back.", "Overwrite", () => _start(n)), null)
                    : _control("native-slot-" + n + "-new", "New climb here", () => _start(n), Loadable(slot) ? null : "primary"));
                if (Loadable(slot) && firstEmpty != null)
                {
                    var target = firstEmpty.Slot;
                    row.Add(_control("native-slot-" + n + "-copy", "Copy to slot " + (target + 1), () => _copy(n, target), null));
                }
                if (Occupied(slot))
                    row.Add(_control("native-slot-" + n + "-delete", "Delete", () => Confirm("Delete slot " + (n + 1) + "?", "This saved climb is removed. There is no way back.", "Delete", () => _delete(n)), null));
                _content.Add(row);
            }
            _content.Add(_control("native-slots-back", "Back to title", _back, null));
            _report?.Invoke();
        }
        private string Heading(OriginalSaveSlotInfo slot)
        {
            var label = "Slot " + (slot.Slot + 1) + " · ";
            switch (slot.State)
            {
                case OriginalSaveSlotState.Empty: return label + "Empty";
                case OriginalSaveSlotState.Corrupt: return label + "Unreadable save";
                default: return label + (_className(slot.Meta?.ClassId) ?? "Unknown wanderer");
            }
        }
        private static string Facts(OriginalSaveSlotInfo slot)
        {
            if (slot.State == OriginalSaveSlotState.Empty) return "No climb saved here";
            if (slot.State == OriginalSaveSlotState.Corrupt || slot.Meta == null) return "This save cannot be read. Its data is kept untouched until you delete or overwrite it.";
            var meta = slot.Meta;
            var text = "Act " + meta.Act + " · Floor " + meta.Floor + " · Seed " + (meta.Seed ?? "—")
                + "\nLast saved " + Stamp(meta.LastSavedUtc) + " · Played " + Playtime(meta.PlaytimeSeconds);
            if (slot.State == OriginalSaveSlotState.RecoveredBackup) text += "\nThe latest save was damaged; Continue opens the previous checkpoint.";
            return text;
        }
        private static string Stamp(string utc) => string.IsNullOrEmpty(utc) ? "unknown" : utc.Replace("T", " ").Replace("Z", " UTC");
        private static string Playtime(long seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? (int)span.TotalHours + "h " + span.Minutes.ToString("00") + "m" : span.Minutes + "m " + span.Seconds.ToString("00") + "s";
        }
        private void Confirm(string question, string detail, string action, Action confirmed)
        {
            _content.Clear();
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("original-slot-confirm");
            box.Add(Text(question, "node-title")); box.Add(Text(detail, "lead"));
            box.Add(_control("native-slot-confirm", action, confirmed, "primary"));
            box.Add(_control("native-slot-cancel", "Back to slots", List, null));
            _content.Add(box); _report?.Invoke();
        }
        private static Label Text(string value, string style)
        {
            var label = new Label(value); label.AddToClassList(style); return label;
        }
    }
}
