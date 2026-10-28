// Core2EventModel.cs — an event node (JS src/ui/screens/event.js mountEvent, and for a quest-chain step
// src/ui/screens/dialogue.js + src/ui/models/DialogueModel.js): the narrative, the responses the run's history opens
// (a priced one the purse cannot pay reads "Cannot afford"), the result text once one is taken, and Continue —
// "Steel yourself" when the response starts a fight. Engine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2EventChoiceView
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public bool Enabled { get; set; }
        /// <summary>"Cannot afford", or why Core 2 refuses the response; null when it can be taken.</summary>
        public string Tag { get; set; }
        public string Question { get; set; }
        public string ResultText { get; set; }
    }

    public sealed class Core2EventModel
    {
        public string EventId { get; private set; }
        public string Eyebrow { get; private set; }
        public string Title { get; private set; }
        public string Text { get; private set; }
        /// <summary>The spoken form (a quest-chain step): the speaker, the player's label and the beats.</summary>
        public bool Spoken => SpeakerName != null;
        public string SpeakerName { get; private set; }
        public string PortraitKey { get; private set; }
        public string PlayerLabel => Core2Text.T("dialogue.player");
        public List<string> Beats { get; } = new List<string>();
        public int Beat { get; private set; }
        public List<Core2EventChoiceView> Choices { get; } = new List<Core2EventChoiceView>();
        public string Status { get; private set; }
        public bool Resolved { get; private set; }
        public string Result { get; private set; }
        public string ContinueLabel { get; private set; }
        public bool ContinueEnabled { get; private set; }
        public string ContinueReason { get; private set; }
        public bool BackEnabled => Spoken && !Resolved && Beat > 0;
        public bool SkipEnabled => Spoken && !Resolved && Beat < Beats.Count - 1;
        public string BackLabel => Core2Text.T("dialogue.back");
        public string SkipLabel => Core2Text.T("dialogue.skip");
        public string Error { get; private set; } = "";
        private readonly Core2RunFlow _flow;

        public Core2EventModel(Core2RunFlow flow) { _flow = flow; Refresh(); }

        /// <summary>JS dialogueBeats: the text split on blank lines, whitespace folded.</summary>
        public static List<string> SplitBeats(string text)
        {
            var beats = Regex.Split(text ?? "", @"\n[ \t]*\n").Select(b => Regex.Replace(b, @"\s+", " ").Trim()).Where(b => b.Length > 0).ToList();
            return beats.Count > 0 ? beats : new List<string> { "" };
        }

        public void Refresh()
        {
            var session = _flow.Session;
            var run = session.Run;
            EventId = run.OpenEvent;
            var def = _flow.Data.Event(EventId);
            Title = (string)def["name"];
            Text = (string)def["text"] ?? "";
            var speaker = Core2Text.Speaker(EventId);
            SpeakerName = speaker?.Name; PortraitKey = speaker?.PortraitKey;
            Eyebrow = Spoken ? Core2Text.T("dialogue.eyebrow") : "Event";
            Beats.Clear(); Beats.AddRange(Spoken ? SplitBeats(Text) : new List<string> { Text });
            Resolved = run.OpenEventChosen;
            Choices.Clear();
            var responses = new List<(bool Affordable, bool Priced)>();
            if (!Resolved)
                foreach (var (choice, affordable, runnable) in session.EventChoices())
                {
                    responses.Add((affordable, choice.Requires != null));
                    Choices.Add(new Core2EventChoiceView
                    {
                        Id = choice.Id, Label = choice.Label, Enabled = affordable && runnable,
                        Tag = !affordable ? (Spoken ? Core2Text.T("dialogue.cannotAfford") : "Cannot afford") : !runnable ? "Configured classes stay fixed for the run" : null,
                        Question = Core2Text.T("dialogue.respond.question", new { label = string.IsNullOrEmpty(choice.Label) ? Core2Text.T("event.defaultOption") : choice.Label }),
                        ResultText = choice.ResultText,
                    });
                }
            if (Resolved)
            {
                // A resumed run has no LastEvent: the history's eventChoice row names the response taken.
                var chosen = _flow.Session.LastEvent?.EventId == EventId ? _flow.Session.LastEvent.ChoiceId
                    : (string)run.History.LastOrDefault(h => (string)h["kind"] == "eventChoice" && (string)h["eventId"] == EventId)?["choiceId"];
                Result = chosen != null ? ResultTextOf(chosen) : Result ?? "";
            }
            var available = responses.Count(r => r.Affordable);
            if (Spoken)
            {
                Status = Resolved ? Core2Text.T("dialogue.status.resolved") : Beat < Beats.Count - 1
                    ? Core2Text.T("dialogue.status.speaking", new { beat = Beat + 1, total = Beats.Count }) : Core2Text.T("dialogue.status.respond");
                ContinueEnabled = Resolved || Beat < Beats.Count - 1;
                ContinueLabel = Resolved && run.CombatEntered != null ? Core2Text.T("dialogue.continue.combat") : Core2Text.T(Resolved ? "dialogue.continue.done" : "dialogue.continue");
                ContinueReason = ContinueEnabled ? null : Core2Text.T("dialogue.continue.reason");
            }
            else
            {
                // JS eventResponseStatus: choose (every response open), limited (a price shuts one), resolved.
                Status = Resolved ? Core2Text.T("event.status.resolved") : available < responses.Count
                    ? Core2Text.T("event.status.limited", new { available, total = responses.Count }) : Core2Text.T("event.status.choose");
                ContinueEnabled = Resolved;
                ContinueLabel = Resolved && run.CombatEntered != null ? Core2Text.T("event.continue.combat") : Core2Text.T("event.continue");
                ContinueReason = ContinueEnabled ? null : Core2Text.T("event.continue.reason");
            }
        }

        private string ResultTextOf(string choiceId) =>
            Core2RunEvents.Choices(_flow.Content, EventId).FirstOrDefault(c => c.Id == choiceId)?.ResultText ?? "";

        /// <summary>The current beat's caption: the speaker's line, or the response's result once answered.</summary>
        public string Caption => Resolved ? Result : Beats[Math.Min(Beat, Beats.Count - 1)];
        /// <summary>Responses are offered on the last beat only (JS dialogueModel offered).</summary>
        public bool ResponsesShown => !Resolved && (!Spoken || Beat == Beats.Count - 1);

        public void Back() { if (BackEnabled) { Beat--; Refresh(); } }
        public void SkipSpeech() { if (SkipEnabled) { Beat = Beats.Count - 1; Refresh(); } }

        public string Choose(string choiceId)
        {
            var error = _flow.ChooseEvent(choiceId);
            Error = error ?? "";
            if (error == null) { Result = ResultTextOf(choiceId); Refresh(); }
            return error;
        }

        /// <summary>Continue: the next beat while speaking; after a response, on to the map or into the fight.</summary>
        public string Continue()
        {
            if (!ContinueEnabled) return ContinueReason;
            if (!Resolved) { Beat++; Refresh(); return null; }
            return _flow.ContinueEvent();
        }
    }
}
