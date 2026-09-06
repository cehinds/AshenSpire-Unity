// RunController.cs — connects campaign rules, view and local save storage.
// ATTACH: one instance on ExpeditionRoot in Scenes/Expedition.unity.
// INSPECTOR: assign PanelSettings; BuildTools creates the initial reference.
// LIFECYCLE: OnEnable binds commands/browser visibility; OnDisable removes both.
// PAUSE: InterruptionState gates an explicit return; CampaignView covers existing UI.
// Change background behavior in Interrupt/ReturnFromInterruption, not CampaignSession.
// Android keyboard focus loss is ignored; native pause and Web document hiding save,
// cancel presentation and suspend audio without changing the player's preferences.
// Update adjusts viewport scaling only. Web uses CSS canvas height via DisplayViewport;
// native safe-area padding stays in screen pixels. CampaignSession owns gameplay state.
// DATA: GameContent/Unity/campaign.json -> Resources/campaign.json via Import Content.
// UI: Presentation/CampaignView.cs and Resources/Expedition.uss. ART: Resources/Art.
// SAVES: CampaignSaveStore owns checksummed primary/backup records per channel.
// Legacy Expedition.v1 saves are preserved under their original keys.
// VERIFY: select hero, play, reload/continue, claim reward, buy gear, complete a run.
// FAILURE: missing content/art -> Import Content; blank UI -> check PanelSettings/Console.
using System;
using AshenSpire.Domain;
using AshenSpire.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Application
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class RunController : MonoBehaviour
    {
        [SerializeField, Tooltip("Shared phone panel settings required by UIDocument.")] private PanelSettings _panelSettings;
        private CampaignDefinition _content; private CampaignSession _session; private CampaignView _view; private CampaignSaveStore _saves;
        private int _screenHeight; private bool _diagnosticsEnabled;
        private int _screenWidth, _displayHeight; private Rect _safeArea;
        private GameAudio _audio;
        private InterruptionState _interruption;
        public void Configure(PanelSettings settings) => _panelSettings = settings;
        private void OnEnable()
        {
            try
            {
                if (_panelSettings == null)
                    throw new InvalidOperationException("Assign ExpeditionRoot PanelSettings.");
                var document = GetComponent<UIDocument>();
                document.panelSettings = _panelSettings;
                var source = Resources.Load<TextAsset>("campaign");
                if (source == null)
                    throw new InvalidOperationException("Import campaign content using the AshenSpire menu.");
                _content = JsonUtility.FromJson<CampaignDefinition>(source.text);
                _content.Validate();
                _audio = GetComponent<GameAudio>();
                if (_audio == null)
                    _audio = gameObject.AddComponent<GameAudio>();
                var channel = "desktop";
                if (Uri.TryCreate(UnityEngine.Application.absoluteURL, UriKind.Absolute, out var uri))
                {
                    channel = "web";
                    foreach (var segment in uri.AbsolutePath.Split('/'))
                    if (segment == "dev" || segment == "test" || segment == "release" || segment == "main")
                    {
                        channel = segment;
                        break;
                    }
                }
                _diagnosticsEnabled = UnityEngine.Application.isEditor || channel == "dev" || (uri != null && uri.IsLoopback);
                _audio.Configure(_content.Audio, _content.Feedback, _diagnosticsEnabled);
                _audio.SetSuspended(false);
                _interruption = new InterruptionState();
                _audio.SetMuted(PlayerPrefs.GetInt("AshenSpire.Muted", 0) == 1);
                _saves = new CampaignSaveStore("AshenSpire.Unity.Campaign.v1." + channel);
                _view = new CampaignView(document.rootVisualElement, _diagnosticsEnabled, PlayerPrefs.GetInt("AshenSpire.ReducedMotion", 0) == 1, PlayerPrefs.GetInt("AshenSpire.FastMotion", 0) == 1, PlayerPrefs.GetInt("AshenSpire.Muted", 0) == 1);
                _view.SetDisplayHeight(DisplayViewport.Height);
                _view.StartRequested += StartRun;
                _view.ContinueRequested += Resume;
                _view.EnterRequested += Enter;
                _view.CardRequested += Play;
                _view.EndTurnRequested += EndTurn;
                _view.RewardRequested += Reward;
                _view.BuyRequested += Buy;
                _view.RestRequested += Rest;
                _view.PotionRequested += Potion;
                _view.RemoveRequested += Remove;
                _view.MenuRequested += Menu;
                _view.SettingsRequested += Settings;
                _view.ReturnRequested += ReturnFromInterruption;
                Menu();
                Debug.Log("ASHENSPIRE_UI_READY");
                _view.MuteRequested += Mute;
                BrowserVisibility.Install(gameObject.name);
            }
            catch (Exception error) { Debug.LogException(error); GetComponent<UIDocument>().rootVisualElement.Add(new Label("The game could not start. " + error.Message)); }
        }
        private void StartRun(string hero, uint seed)
        {
            Bind(new CampaignSession(_content, hero, seed));
            Refresh();
        }
        private void Resume()
        {
            try
            {
                Bind(new CampaignSession(_content, _saves.Load()));
                Refresh();
            }
            catch (Exception error) { Debug.LogWarning(error.Message); _view.Title(_content, false, "This save could not be read. The original records are preserved."); }
        }
        private void Bind(CampaignSession value)
        {
            if (_session != null)
                _session.Changed -= Refresh;
            _session = value;
            _session.Changed += Refresh;
        }
        private void Enter(int route) => _session?.Enter(route);
        private void Play(int index)
        {
            if (_session == null || index < 0 || index >= _session.State.Hand.Count)
                return;
            var card = _session.Card(_session.State.Hand[index]);
            var before = new FeedbackSnapshot(_session.State);
            if (_session.Play(index))
            {
                var cue = _content.Feedback?.ForCard(card);
                Present(cue, before.Compare(_session.State));
            }
        }
        private void EndTurn()
        {
            if (_session == null)
                return;
            var intent = _session.Intent.Operation;
            var before = new FeedbackSnapshot(_session.State);
            if (_session.EndTurn())
            {
                var outcome = before.Compare(_session.State);
                outcome.Action = _session.State.EnemyHealth <= 0 ? "POISON" : "ENEMY " + intent.ToUpperInvariant();
                Present(_content.Feedback?.Cue(outcome.Hurt > 0 ? "hit" : "guard"), outcome, true);
            }
        }
        private void Present(FeedbackCue cue, FeedbackOutcome outcome, bool enemyTurn = false)
        {
            if (cue == null) return;
            _view.Feedback(cue, outcome, enemyTurn);
            _audio.Play(cue.Id);
        }
        private void Reward(string id)
        {
            if (_session != null && _session.Reward(id))
                _audio.Play("reward");
        }
        private void Buy(string id) => _session?.Buy(id); private void Rest() => _session?.Rest(); private void Remove(int index) => _session?.RemoveCard(index);
        private void Potion()
        {
            if (_session == null) return;
            var before = new FeedbackSnapshot(_session.State);
            if (_session.DrinkPotion()) Present(_content.Feedback?.Cue("heal"), before.Compare(_session.State));
        }
        private void Mute(bool muted)
        {
            PlayerPrefs.SetInt("AshenSpire.Muted", muted ? 1 : 0);
            PlayerPrefs.Save();
            _audio.SetMuted(muted);
        }
        private void Menu()
        {
            Save();
            _view.Title(_content, _saves.HasSave);
        }
        private void Settings(bool reduced, bool fast)
        {
            PlayerPrefs.SetInt("AshenSpire.ReducedMotion", reduced ? 1 : 0);
            PlayerPrefs.SetInt("AshenSpire.FastMotion", fast ? 1 : 0);
            PlayerPrefs.Save();
        }
        private void Refresh()
        {
            Save();
            _view.Render(_session);
            if (_diagnosticsEnabled)
                Debug.Log("ASHENSPIRE_CAMPAIGN " + JsonUtility.ToJson(_session.State));
        }
        private void Save()
        {
            if (_session != null && _saves != null)
                _saves.Save(_session.State);
        }
        private void OnApplicationPause(bool paused)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Interrupt(InterruptionSource.NativePause, paused);
#endif
        }
        private void OnApplicationFocus(bool focused)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // Android soft keyboards emit focus loss without backgrounding the game.
            if (!UnityEngine.Application.isMobilePlatform)
                Interrupt(InterruptionSource.DesktopFocus, !focused);
#endif
        }
        // Called only by the Web lifecycle adapter, not a gameplay command endpoint.
        [UnityEngine.Scripting.Preserve]
        public void OnBrowserVisibilityChanged(int hidden)
        {
            Interrupt(InterruptionSource.BrowserHidden, hidden != 0);
        }
        private void Interrupt(InterruptionSource source, bool active)
        {
            if (_view == null || _interruption == null) return;
            var first = !_interruption.IsInterrupted;
            if (!_interruption.Set(source, active)) return;
            _audio.SetSuspended(true);
            _view.ShowInterruption(_interruption.CanReturn);
            if (first)
            {
                try { Save(); }
                catch (Exception error) { Debug.LogWarning("Background save failed: " + error.Message); }
            }
            ReportInterruption();
        }
        private void ReturnFromInterruption()
        {
            if (!_interruption.TryReturn()) return;
            _view.HideInterruption();
            _audio.SetSuspended(false);
            ReportInterruption();
        }
        [Serializable]
        private sealed class InterruptionReport
        {
            public bool Blocked, CanReturn, AudioPlaying, Muted;
        }
        private void ReportInterruption()
        {
            if (_diagnosticsEnabled)
                Debug.Log("ASHENSPIRE_INTERRUPTION " + JsonUtility.ToJson(new InterruptionReport {
                    Blocked = _interruption.IsInterrupted, CanReturn = _interruption.CanReturn,
                    AudioPlaying = _audio.IsPlaying, Muted = _audio.IsMuted }));
        }
        private void Update()
        {
            var displayHeight = DisplayViewport.Height;
            if (_panelSettings == null || (_screenHeight == Screen.height && _screenWidth == Screen.width && _safeArea == Screen.safeArea && _displayHeight == displayHeight))
                return;
            _screenHeight = Screen.height;
            _screenWidth = Screen.width;
            _safeArea = Screen.safeArea;
            _displayHeight = displayHeight;
            var referenceHeight = ViewportLayout.ReferenceHeight(displayHeight);
            _panelSettings.referenceResolution = new Vector2Int(430, referenceHeight);
            _view?.SetDisplayHeight(displayHeight);
            var scale = (float)referenceHeight / Math.Max(1, Screen.height);
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.style.paddingTop = (Screen.height - _safeArea.yMax) * scale;
            root.style.paddingBottom = _safeArea.yMin * scale;
            root.style.paddingLeft = _safeArea.xMin * scale;
            root.style.paddingRight = (Screen.width - _safeArea.xMax) * scale;
        }
        private void OnDisable()
        {
            BrowserVisibility.Remove();
            Save();
            if (_audio != null) _audio.SetMuted(true);
            if (_session != null)
                _session.Changed -= Refresh;
            if (_view == null)
                return;
            _view.Dispose();
            _view.ReturnRequested -= ReturnFromInterruption;
            _view.MuteRequested -= Mute;
            _view.StartRequested -= StartRun;
            _view.ContinueRequested -= Resume;
            _view.EnterRequested -= Enter;
            _view.CardRequested -= Play;
            _view.EndTurnRequested -= EndTurn;
            _view.RewardRequested -= Reward;
            _view.BuyRequested -= Buy;
            _view.RestRequested -= Rest;
            _view.PotionRequested -= Potion;
            _view.RemoveRequested -= Remove;
            _view.MenuRequested -= Menu;
            _view.SettingsRequested -= Settings;
            _view = null;
            _interruption = null;
            _session = null;
        }
    }
}
