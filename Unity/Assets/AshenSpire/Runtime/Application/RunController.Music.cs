// RunController.Music.cs — music hooks, part of RunController (F08).
// ATTACH: no additional MonoBehaviour of its own; MusicPlayer is added to ExpeditionRoot.
// MODIFY: which screen plays which context lives in MusicSceneMap/MusicDirector (Domain);
// this file only forwards run changes. Co-op screens keep the title bed (not wired yet).
using AshenSpire.Domain;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private MusicPlayer _music;
        private void AttachMusic() => _music = MusicPlayer.Attach(gameObject, PlayerPrefs.GetInt("AshenSpire.Muted", 0) == 1);
        private void MusicTitle() { if (_music != null) _music.Enter(MusicScene.Title); }
        private void MusicCampaign() { if (_music != null && _session != null) _music.Enter(MusicSceneMap.ForCampaignPhase(_session.State.Phase.ToString())); }
        private void MusicNative()
        {
            if (_music == null || _originalGame == null) return;
            var phase = _originalGame.Phase;
            var pool = phase == AshenSpire.Domain.Original.OriginalRunPhase.Combat ? (string)_originalGame.Room["pool"] : null;
            _music.Enter(MusicSceneMap.ForNativePhase(phase.ToString(), pool), _originalGame.ActNumber);
        }
        private void MusicMuted(bool muted) { if (_music != null) _music.SetMuted(muted); }
        private void MusicSuspended(bool suspended) { if (_music != null) _music.SetSuspended(suspended); }
        private void MusicStop() { if (_music != null) _music.Enter(MusicScene.Quit); }
    }
}
