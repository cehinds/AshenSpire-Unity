// CampaignView.Coop.cs — companion connection and party views in the shared UI shell.
// Modify labels/fields here; transport and saved credentials belong to RunController.
// Password fields are never added to diagnostics. Party commands carry no player stats.
using System;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        private OriginalCoopPanel _coopPanel;
        private readonly CoopPanelState _coopPanelState = new CoopPanelState();
        public void CoopConnect(JObject draft, bool hasCharacter, bool canRejoin, string notice,
            Action create, Action<bool> connect, Action back)
        {
            Shell("CLIMB TOGETHER", "A SHARED CLIMB · YOUR OWN HAND");
            _body.Add(Text("Run the AshenSpire companion on the host computer. Everyone joins its address and uses the invitation code supplied by the host.", "lead"));
            Field("coop-endpoint", "Companion address", "endpoint");
            Field("coop-invite", "Invitation code", "joinToken", true);
            Field("coop-host-key", "Host key (host only)", "hostToken", true);
            if (notice != null) _body.Add(Text(notice, "notice"));
            AddButton("coop-create", hasCharacter ? "Change wanderer" : "Choose your wanderer", create);
            if (hasCharacter) AddButton("coop-connect", "Join with this wanderer", () => connect(false), "primary");
            if (canRejoin) AddButton("coop-rejoin", "Rejoin saved seat", () => connect(true), "primary");
            _body.Add(Text("The host saves the shared run. Your seat can reconnect after closing this page. An HTTPS page needs a secure wss:// companion address; for local play, open the Web build served by the companion.", "caption"));
            AddButton("coop-back", "Back to title", back); Report();
            void Field(string id, string label, string key, bool secret = false)
            {
                var field = new TextField(label) { name = id, value = (string)draft[key] ?? "", isPasswordField = secret, maxLength = 512 };
                field.AddToClassList("seed-field"); field.RegisterValueChangedCallback(e => draft[key] = e.newValue.Trim()); _body.Add(field);
            }
        }
        public void CoopLobby(JObject snapshot, bool host, Action<bool> ready, Action start, Action<uint> seedChanged, Action<bool> endlessChanged, Action back, string notice = null, Action<string> removeSeat = null)
        {
            Shell("CLIMB TOGETHER", "WAITING AT THE FOOT OF THE SPIRE");
            var lobby = snapshot["lobby"];
            _body.Add(Text("Seed " + lobby?["seed"] + ((bool?)lobby?["endless"] == true ? " · Endless" : " · Three acts"), "heading"));
            foreach (var seat in lobby?["seats"] ?? new JArray())
            {
                _body.Add(Text((string)seat["name"] + " · " + ((bool?)seat["connected"] == false ? "disconnected" : (bool?)seat["ready"] == true ? "ready" : "choosing"), "stat"));
                // Only abandoned pre-start guest seats can be removed. The host
                // independently checks these predicates before changing its roster.
                if (host && (bool?)lobby?["started"] != true && (bool?)seat["connected"] == false && (bool?)seat["host"] != true && removeSeat != null)
                {
                    var id = (string)seat["id"];
                    AddButton("coop-remove-seat-" + id, "Remove disconnected seat: " + (string)seat["name"], () => removeSeat(id));
                }
            }
            if (notice != null) _body.Add(Text(notice, "notice"));
            if (host)
            {
                var seed = new TextField("Shared seed (number)") { name = "coop-seed", value = lobby?["seed"]?.ToString() ?? "1" }; seed.AddToClassList("seed-field"); _body.Add(seed);
                var seedError = Text("", "notice"); _body.Add(seedError);
                AddButton("coop-seed-apply", "Set shared seed", () => { if (uint.TryParse(seed.value, out var parsed)) seedChanged(parsed); else { seedError.text = "Enter a whole number from 0 to 4294967295."; Report(); } });
                var endless = new Toggle("Endless shared climb") { name = "coop-endless", value = (bool?)lobby?["endless"] == true }; endless.AddToClassList("setting"); endless.RegisterValueChangedCallback(e => endlessChanged(e.newValue)); _body.Add(endless);
            }
            AddButton("coop-ready", "I'm ready", () => ready(true), "primary");
            AddButton("coop-not-ready", "Wait for me", () => ready(false));
            if (host) AddButton("coop-start", "Begin shared climb", start, "primary");
            AddButton("coop-leave", "Disconnect and return to title", back); Report();
        }
        public void CoopGame(JObject snapshot, OriginalContentCatalog catalog, JObject supplement, Action<JObject> send, Action back)
        {
            Shell("ASHEN SPIRE", "THE SHARED CLIMB");
            _coopPanel = new OriginalCoopPanel(_body, snapshot, catalog, supplement, send, () => Report(), back, _coopPanelState);
        }
        public void CoopError(string message) { _coopPanel?.ShowError(message); }
    }
}
