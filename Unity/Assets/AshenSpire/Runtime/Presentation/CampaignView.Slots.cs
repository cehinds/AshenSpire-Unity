// CampaignView.Slots.cs — the save-slot screen in the shared UI shell.
// The title's "Load" entry raises SlotsRequested; RunController answers with Slots(...).
// Layout and confirmation live in OriginalSlotPanel; storage stays in RunController.Slots.
using System;
using System.Collections.Generic;
using AshenSpire.Domain.Original;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        public event Action SlotsRequested;
        public event Action WebImportRequested;
        public bool WebSaveImportVisible => _body?.Q<TextField>("native-web-import-json") != null;
        public void Slots(IReadOnlyList<OriginalSaveSlotInfo> slots, Func<string, string> className, string notice,
            Action<int> load, Action<int> start, Action<int> delete, Action<int, int> copy)
        {
            Shell("ASHENEDSPIRE", "SAVED CLIMBS · THREE SLOTS");
            _body.AddToClassList("slots-screen");
            _body.Add(Control("native-web-import", "Import original-game save", () => WebImportRequested?.Invoke(), "secondary-button"));
            _ = new OriginalSlotPanel(_body, slots, className, notice, load, start, delete, copy,
                () => MenuRequested?.Invoke(), (id, label, clicked, style) => Control(id, label, clicked, style), () => Report());
        }
        public void WebSaveImport(string notice, Action<string> preview, Action back, Action chooseFile = null, Action<int> browserSlot = null)
        {
            Shell("ASHENEDSPIRE", "IMPORT AN ORIGINAL SAVE");
            _body.Add(new Label("Paste the JSON from an original AshenSpire run save, exported run archive or exported profile. Run import supports compatible map checkpoints; finish combat, rewards or shopping in the original game first. Profile import adds run history, unlocks and found armaments. Your original file and existing slots stay untouched.") { style = { whiteSpace = WhiteSpace.Normal } });
            if (chooseFile != null) _body.Add(Control("native-web-import-file", "Choose save file", chooseFile, "secondary-button"));
            if (browserSlot != null)
            {
                _body.Add(new Label("Played the original on this website in this browser? Choose its slot below. Saves from another website or browser require a file.") { style = { whiteSpace = WhiteSpace.Normal } });
                for (var i = 0; i < 3; i++) { var slot = i; _body.Add(Control("native-web-import-browser-" + i, "Read original slot " + (i + 1), () => browserSlot(slot), "secondary-button")); }
            }
            if (!string.IsNullOrEmpty(notice)) _body.Add(new Label(notice) { style = { whiteSpace = WhiteSpace.Normal } });
            var field = new TextField("Original save JSON") { name = "native-web-import-json", multiline = true, maxLength = OriginalWebSaveImport.MaximumBytes };
            field.style.height = 170; _body.Add(field);
            _body.Add(Control("native-web-import-preview", "Check save", () => preview(field.value), "primary-button"));
            _body.Add(Control("native-web-import-back", "Back", back, "secondary-button"));
            Report();
        }
        /// <summary>Profile merge preview. A null action hides its button (nothing new, or no mapped settings).</summary>
        public void WebProfileImportPreview(string summary, string unmappedSettings, Action importProfile, Action importWithSettings, Action back)
        {
            Shell("ASHENEDSPIRE", "REVIEW YOUR PROFILE IMPORT");
            _body.Add(new Label(summary) { style = { whiteSpace = WhiteSpace.Normal } });
            _body.Add(new Label("Imported history and unlocks are added to your AshenedSpire profile; nothing already there is removed, and importing the same profile again adds nothing.") { style = { whiteSpace = WhiteSpace.Normal } });
            if (!string.IsNullOrEmpty(unmappedSettings)) _body.Add(new Label(unmappedSettings) { style = { whiteSpace = WhiteSpace.Normal } });
            if (importProfile != null) _body.Add(Control("native-web-profile-import-confirm", "Import history and unlocks", importProfile, "primary-button"));
            if (importWithSettings != null) _body.Add(Control("native-web-profile-import-settings", importProfile != null ? "Import with settings" : "Import settings", importWithSettings, importProfile != null ? "secondary-button" : "primary-button"));
            _body.Add(Control("native-web-import-cancel", "Cancel", back, "secondary-button"));
            Report();
        }
        public void WebSaveImportPreview(string summary, Action confirm, Action back)
        {
            Shell("ASHENEDSPIRE", "REVIEW YOUR IMPORT");
            _body.Add(new Label(summary) { style = { whiteSpace = WhiteSpace.Normal } });
            _body.Add(new Label("The original map, resources, card identities and random state are preserved. This copy will continue in AshenedSpire using its supported original rules. Profile history and unlocks are separate and are not replaced.") { style = { whiteSpace = WhiteSpace.Normal } });
            _body.Add(Control("native-web-import-confirm", "Import into empty slot", confirm, "primary-button"));
            _body.Add(Control("native-web-import-cancel", "Cancel", back, "secondary-button"));
            Report();
        }
    }
}
