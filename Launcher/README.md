# Force Bond launcher 0.2

The existing WinForms launcher now includes a live session panel, user settings,
diagnostics, a visible build version, and a subdued Peragus loading-screen
background. No launcher sounds are used.

## Status contract

The native runtime publishes `session-<PID>.ini` in
`%LOCALAPPDATA%\KOTOR2_Coop` once per second using an atomic file replacement.
The launcher validates schema, PID, process creation FILETIME and heartbeat age
before displaying its contents. It ignores stale files and any status after
the matching game process has exited. A window-thread timer drives runtime
polling even when the user is not moving the mouse.

Game and DLL presence alone do not imply world readiness. The World step needs
the script bridge and character profile. Partner needs the P2P handshake. Sync
additionally needs a script-confirmed NPC and remote state less than 3 seconds
old. This is evidence of an active movement link, not validation of combat or
campaign synchronization. Missing/incompatible telemetry stays unknown.

## Settings contract

`forcebond_user.ini` lives in the same user directory, with ASCII keys under
`[ForceBond]`: `ChatKey`, `VoiceKey`, `VoiceVolume`, `DiscordEnabled`. Key values
are Windows virtual-key integers. The launcher and runtime accept matching
bounded key lists and reserve F8/F9 for Steam. The runtime reloads settings once
per second. Voice volume scales received PCM samples without changing the
shared output-device volume. Discord is explicitly cleared when disabled.

Launcher 0.2.3 adds `Language=en|uk|ru` in the same INI. English is the default
for missing or unsupported values. Language codes are ASCII; the file remains
compatible with the native Windows INI reader and all earlier input settings.
Settings offers English / Українська / Русский. Saving changes the launcher
immediately and persists across restarts. This setting does not change game
language, Discord activity or in-game overlay text. Technical log entries and
runtime telemetry keys remain unchanged. Localization.cs contains the launcher
strings, with English fallback and bindings for live status/button updates.

The recovered overlay still consumes T events; the game-message hook maps the
chosen chat key to T before dispatch, while preserving text-input handling.
DirectInput filters the configured gameplay hotkeys. Actual alternate-key
behavior and voice playback must still be exercised in KOTOR II.

## Checks and previews

`OdysseyMP/Checks/check_bridge.py --game <installation>` exercises the global
catalog/byte bridge, Feedback consumer, settings compatibility with the native
Windows INI reader, and session identity/freshness/readiness rules.

The launcher supports developer-only screenshot switches `--preview`,
`--preview-settings`, and `--preview-diagnostics`, followed by an absolute PNG
path. They render an offscreen window and do not install, inject, or start the
game. Normal launch does not use these switches.
Add `--language=en`, `--language=uk` or `--language=ru` to render a preview in a
particular language without modifying user settings.
