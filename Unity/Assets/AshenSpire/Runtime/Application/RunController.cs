// RunController.cs — connects campaign rules, view and local save storage.
// ATTACH: one instance on ExpeditionRoot in Scenes/Expedition.unity.
// INSPECTOR: assign PanelSettings; BuildTools creates the initial reference.
// LIFECYCLE: OnEnable binds commands; OnDisable unsubscribes/disposes view/saves; pause saves.
// Update adjusts viewport scaling only. CampaignSession owns all gameplay state.
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
        private int _screenWidth; private Rect _safeArea;
        private GameAudio _audio;
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
                _audio.Configure(_content.Audio);
                _audio.SetMuted(PlayerPrefs.GetInt("AshenSpire.Muted", 0) == 1);
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
                _saves = new CampaignSaveStore("AshenSpire.Unity.Campaign.v1." + channel);
                _view = new CampaignView(document.rootVisualElement, _diagnosticsEnabled, PlayerPrefs.GetInt("AshenSpire.ReducedMotion", 0) == 1, PlayerPrefs.GetInt("AshenSpire.FastMotion", 0) == 1, PlayerPrefs.GetInt("AshenSpire.Muted", 0) == 1);
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
                Menu();
                Debug.Log("ASHENSPIRE_UI_READY");
                _view.MuteRequested += Mute;
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
            var attack = _session.Card(_session.State.Hand[index]).HasTag("attack");
            if (_session.Play(index))
            {
                _view.Animate(attack ? "attack1" : "guard", attack);
                _audio.Play(attack ? "attack" : "guard");
            }
        }
        private void EndTurn()
        {
            if (_session == null)
                return;
            var health = _session.State.Health;
            if (_session.EndTurn() && _session.State.Health < health)
            {
                _view.Animate("hit");
                _audio.Play("hit");
            }
        }
        private void Reward(string id)
        {
            if (_session != null && _session.Reward(id))
                _audio.Play("reward");
        }
        private void Buy(string id) => _session?.Buy(id); private void Rest() => _session?.Rest(); private void Potion() => _session?.DrinkPotion(); private void Remove(int index) => _session?.RemoveCard(index);
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
            if (paused)
                Save();
        }
        private void Update()
        {
            if (_panelSettings == null || (_screenHeight == Screen.height && _screenWidth == Screen.width && _safeArea == Screen.safeArea))
                return;
            _screenHeight = Screen.height;
            _screenWidth = Screen.width;
            _safeArea = Screen.safeArea;
            var referenceHeight = Screen.height < 600 ? Screen.height : 900;
            _panelSettings.referenceResolution = new Vector2Int(430, referenceHeight);
            var scale = (float)referenceHeight / Math.Max(1, Screen.height);
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.style.paddingTop = (Screen.height - _safeArea.yMax) * scale;
            root.style.paddingBottom = _safeArea.yMin * scale;
            root.style.paddingLeft = _safeArea.xMin * scale;
            root.style.paddingRight = (Screen.width - _safeArea.xMax) * scale;
        }
        private void OnDisable()
        {
            Save();
            if (_session != null)
                _session.Changed -= Refresh;
            if (_view == null)
                return;
            _view.Dispose();
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
        }
    }
}
