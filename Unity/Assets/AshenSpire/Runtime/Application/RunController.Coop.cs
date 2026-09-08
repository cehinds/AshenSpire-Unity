// RunController.Coop.cs — shared-run application adapter; part of RunController.
// ATTACH: no extra component; ExpeditionRoot's RunController creates a dedicated
// NativeLanClient object on demand. It owns transport subscriptions and destroys
// only that object when leaving. Do not attach this partial file independently.
// MODIFY: lobby presentation in CampaignView.Coop; gameplay in OriginalCoopRun;
// protocol/authentication in Tools/NativeLan. The client never resolves a battle.
// SAVE: endpoint-keyed rejoin receipts live in PlayerPrefs; invitation/host keys
// are not persisted or logged. The companion durably owns the shared run itself.
// VERIFY: two clients, ready/start, route vote, card, disconnect/rejoin, host restart.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private NativeLanClient _lan;
        private JObject _coopDraft, _coopCharacter, _coopHello, _coopSnapshot;
        private bool _coopActive, _coopHost;
        private readonly CoopCommandGate _coopCommands = new CoopCommandGate();
        private int _coopRequest, _coopDiagnostic;
        private string _coopSeat, _coopEndpoint;
        private const string CoopReceiptsKey = "AshenSpire.Unity.Coop.Receipts.v1";
        private void OpenCoop()
        {
            LoadOriginalProfile();
            if (_coopDraft == null)
            {
                var endpoint = PlayerPrefs.GetString("AshenSpire.Unity.Coop.Endpoint", "ws://127.0.0.1:8795/lan");
                _coopDraft = new JObject { ["endpoint"] = endpoint, ["joinToken"] = "", ["hostToken"] = "" };
            }
            ShowCoopConnect();
        }
        private JObject CoopReceipts()
        {
            try { return JObject.Parse(PlayerPrefs.GetString(CoopReceiptsKey, "{}")); }
            catch (JsonException) { return new JObject(); }
        }
        private void ShowCoopConnect(string notice = null)
        {
            _view.CoopConnect(_coopDraft, _coopCharacter != null, CoopReceipts().Properties().Any(), notice, () =>
            {
                // The companion validates a baseline character against its own data.
                // Earned local kits cannot be asserted as trusted host unlocks.
                _view.NativeCreation(_originalContent, new AttributeProgression(OriginalRules("progression")), OriginalRules("mechanics"), (player, seed) =>
                {
                    _coopCharacter = player; _coopCharacter["seed"] = seed; ShowCoopConnect("Wanderer selected. Join the companion when ready.");
                }, new OriginalProfile(_originalContent).Snapshot());
            }, ConnectCoop, () => { CloseCoop(); Menu(); });
        }
        private void ConnectCoop(bool rejoin)
        {
            CloseCoop();
            try
            {
                _coopEndpoint = ((string)_coopDraft["endpoint"] ?? "").Trim();
                if (!Uri.TryCreate(_coopEndpoint, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != "ws" && endpoint.Scheme != "wss")) throw new ArgumentException("Enter the companion's ws:// or wss:// address.");
                _coopHello = new JObject();
                if (rejoin)
                {
                    var token = (string)CoopReceipts()[_coopEndpoint];
                    if (string.IsNullOrEmpty(token)) throw new ArgumentException("No saved seat exists for this address. Join with a wanderer first.");
                    _coopHello["resumeToken"] = token;
                }
                else
                {
                    if (_coopCharacter == null) throw new ArgumentException("Choose your wanderer first.");
                    var custom = OriginalCustomRunRules.Normalize(_coopCharacter["custom"] as JObject);
                    if ((int)custom["ascension"] != 0 || (string)custom["deckMode"] != "standard" || custom["mapShape"] is JObject || ((JObject)custom["mods"]).Properties().Any(p => (bool)p.Value)) throw new ArgumentException("Shared climbs use standard creation. The host chooses endless for the whole party.");
                    var setup = new JObject { ["classId"] = _coopCharacter["classId"].DeepClone(), ["modeId"] = _coopCharacter["attributeMode"].DeepClone(), ["attributes"] = _coopCharacter["attributes"].DeepClone(), ["kitId"] = _coopCharacter["startingKitId"]?.DeepClone() };
                    if ((bool?)_coopCharacter["startingKitSnapshot"]?["customized"] == true)
                        setup["startingHands"] = new JObject { ["leftHand"] = _coopCharacter["startingKitSnapshot"]["leftHand"].DeepClone(), ["rightHand"] = _coopCharacter["startingKitSnapshot"]["rightHand"].DeepClone() };
                    setup["startingArmourId"] = _coopCharacter["loadout"]["creationArmourGrant"]["id"].DeepClone();
                    setup["startingRelicId"] = _coopCharacter["relicIds"][0].DeepClone();
                    setup["keepsakeId"] = _coopCharacter["keepsakeId"]?.DeepClone() ?? new JValue("none");
                    setup["customization"] = _coopCharacter["customization"]?.DeepClone() ?? new JObject { ["name"] = "Forsaken", ["glyph"] = "⚔", ["tint"] = "gold" };
                    // Starting identities are checked against host content; no HP,
                    // cards, resources or client-provided profile enter the protocol.
                    _coopHello["setup"] = setup;
                    var name = (string)_coopCharacter["customization"]?["name"];
                    if (string.IsNullOrWhiteSpace(name)) name = "Forsaken";
                    _coopHello["name"] = name.Substring(0, Math.Min(18, name.Length));
                    _coopHello["joinToken"] = (string)_coopDraft["joinToken"];
                    if (!string.IsNullOrEmpty((string)_coopDraft["hostToken"])) _coopHello["hostToken"] = (string)_coopDraft["hostToken"];
                }
                var receiver = new GameObject("AshenSpire.NativeLan"); _lan = receiver.AddComponent<NativeLanClient>();
                _lan.Opened += CoopOpened; _lan.MessageReceived += CoopMessage; _lan.Closed += CoopDisconnected; _lan.Failed += CoopDisconnected;
                _coopActive = true; _lan.Connect(_coopEndpoint);
                ShowCoopConnect("Connecting to the companion…");
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException) { CloseCoop(); ShowCoopConnect(error.Message); }
        }
        private void CoopOpened() => SendCoopEnvelope("hello", _coopHello);
        private void SendCoopEnvelope(string type, JObject payload, long? sequence = null)
        {
            var requestId = "unity-" + (++_coopRequest);
            try
            {
                if (sequence.HasValue) _coopCommands.Begin(requestId, sequence.Value);
                _lan.Send(new JObject { ["v"] = 1, ["type"] = type, ["requestId"] = requestId, ["payload"] = payload }.ToString(Formatting.None));
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException) { _coopCommands.Reject(requestId); _view.CoopError(error.Message); }
        }
        private void CoopMessage(string text)
        {
            try
            {
                var message = JObject.Parse(text); var type = (string)message["type"]; var payload = message["payload"] as JObject ?? new JObject();
                if (type == "welcome")
                {
                    _coopSeat = (string)payload["seatId"]; _coopHost = (bool?)payload["host"] == true;
                    var receipts = CoopReceipts(); receipts[_coopEndpoint] = payload["resumeToken"].DeepClone();
                    PlayerPrefs.SetString(CoopReceiptsKey, receipts.ToString(Formatting.None)); PlayerPrefs.SetString("AshenSpire.Unity.Coop.Endpoint", _coopEndpoint); PlayerPrefs.Save();
                    _coopDraft["hostToken"] = ""; _coopDraft["joinToken"] = ""; _coopHello = null;
                }
                else if (type == "state")
                {
                    _coopSnapshot = payload;
                    _coopCommands.Observe((long?)((payload["game"] as JObject)?["local"] as JObject)?["sequence"] ?? 0);
                    SaveCoopProgress();
                    if (!_interruption.IsInterrupted) RenderCoop();
                    ReportCoop();
                }
                else if (type == "receipt" && (bool?)payload["ok"] == true)
                    _coopCommands.Accept((string)message["requestId"]);
                else if (type == "error" || (type == "receipt" && (bool?)payload["ok"] == false))
                {
                    _coopCommands.Reject((string)message["requestId"]); var error = (string)payload["error"] ?? (string)payload["message"] ?? "The companion refused this command.";
                    if (_coopSnapshot == null) ShowCoopConnect(error);
                    else if ((bool?)_coopSnapshot["lobby"]?["started"] != true) RenderCoop(error);
                    else _view.CoopError(error);
                }
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException || error is NullReferenceException)
            { CoopDisconnected("The companion sent an invalid game view. Rejoin to restore its saved state."); }
        }
        private void RenderCoop(string notice = null)
        {
            if (_coopSnapshot == null) return;
            if ((bool?)_coopSnapshot["lobby"]?["started"] != true)
                _view.CoopLobby(_coopSnapshot, _coopHost, ready => SendCoopEnvelope("ready", new JObject { ["ready"] = ready }), () => SendCoopEnvelope("start", new JObject { ["sequence"] = 1 }), seed => SendCoopEnvelope("seed", new JObject { ["seed"] = seed }), endless => SendCoopEnvelope("endless", new JObject { ["enabled"] = endless }), LeaveCoop, notice, seatId => SendCoopEnvelope("removeSeat", new JObject { ["seatId"] = seatId }));
            else _view.CoopGame((JObject)_coopSnapshot["game"], _originalContent, OriginalRules("event-choices"), CoopIntent, LeaveCoop);
        }
        private void CoopIntent(JObject intent)
        {
            if (_coopCommands.IsPending) { _view.CoopError("Waiting for the companion to confirm your previous action."); return; }
            var sequence = ((long?)((_coopSnapshot["game"] as JObject)?["local"] as JObject)?["sequence"] ?? 0) + 1;
            SendCoopEnvelope("intent", new JObject { ["sequence"] = sequence, ["intent"] = intent }, sequence);
        }
        private void SaveCoopProgress()
        {
            var local = (_coopSnapshot?["game"] as JObject)?["local"] as JObject;
            if (_profile == null || !(local?["run"] is JObject run) || (local["catchup"] as JArray)?.Count > 0) return;
            var changed = false;
            foreach (var id in run["foundArmaments"] ?? new JArray()) { _profile.CollectArmament(run, (string)id, "coop"); changed = true; }
            if ((string)_coopSnapshot["game"]["scene"]["kind"] == "complete")
            {
                _profile.Finish((string)run["runId"], run, (string)_coopSnapshot["game"]["scene"]["result"] == "victory"); changed = true;
            }
            if (changed) _profileSaves.Save(_profile.Snapshot());
        }
        private void ReportCoop()
        {
            if (!_diagnosticsEnabled) return;
                // Tokens and hello data never enter diagnostics. This is an observer
            // snapshot, not a browser command or a writable gameplay interface.
            // Escape before chunking so names and glyphs cannot leave isolated
            // UTF-16 surrogates in a Web console message. No network/save change.
            var json = JsonConvert.SerializeObject(_coopSnapshot,
                new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii });
            var sequence = ++_coopDiagnostic; var count = (json.Length + 2499) / 2500;
            for (var index = 0; index < count; index++) Debug.Log("ASHENSPIRE_COOP_STATE_CHUNK " + new JObject { ["sequence"] = sequence, ["index"] = index, ["count"] = count, ["text"] = json.Substring(index * 2500, Math.Min(2500, json.Length - index * 2500)) }.ToString(Formatting.None));
        }
        private void CoopDisconnected(string message)
        {
            if (!_coopActive) return;
            var saved = !string.IsNullOrEmpty(_coopEndpoint) && CoopReceipts()[_coopEndpoint] != null;
            CloseCoop(); ShowCoopConnect(OriginalCardText.Humanize(message.Replace('_', ' ')) + (saved ? " Your seat remains saved on the host." : " Check the companion address and invitation code, then try again."));
        }
        private void LeaveCoop() { CloseCoop(); Menu(); }
        private void CloseCoop()
        {
            _coopActive = false; _coopCommands.Reset(); _coopSnapshot = null;
            if (_lan == null) return;
            _lan.Opened -= CoopOpened; _lan.MessageReceived -= CoopMessage; _lan.Closed -= CoopDisconnected; _lan.Failed -= CoopDisconnected;
            _lan.Close(); Destroy(_lan.gameObject); _lan = null;
        }
    }
}
