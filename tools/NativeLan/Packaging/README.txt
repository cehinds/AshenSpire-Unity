AshenSpire native co-op companion for Windows

1. Extract this folder next to the matching game's Web folder.
2. Double-click Start-Companion.cmd. Keep its window open while playing.
3. Open http://127.0.0.1:8795/ and choose the game's cooperative mode.

The console displays the join key for guests and a separate host key for hosting.
Do not share the host key with guests. Resume keys stay with each player's browser.
A .NET installation, Unity Editor, and source repository are not required.

Different Web location:
  powershell -File .\Start-Companion.ps1 -WebRoot C:\Games\AshenSpire\Web

Players on your trusted local network:
  powershell -File .\Start-Companion.ps1 -ListenOnLan
Use this computer's LAN IP in other browsers. This tool does not change firewall
or router settings. HTTPS Pages and a local HTTP WebSocket are different origins;
use the companion's own HTTP game page for direct local play.

Saves live in State\host-state.json with an atomic backup. Restart with the same
state file to resume. Keep the entire State folder private and carry it forward
when updating this companion. To use a stable folder, pass -StatePath explicitly.

Corrupt saves do not reset themselves. Explicit verified backup recovery:
  powershell -File .\Start-Companion.ps1 -RecoverBackup
The damaged primary is preserved separately. Backup recovery can lose the latest
command if it existed only in the damaged primary.

BuildStamp.json identifies version/build/source and content/runtime file hashes.
