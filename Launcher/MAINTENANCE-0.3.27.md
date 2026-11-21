# Launcher maintenance — 0.3.27

Fixed the startup status that remained at Checking for updates: CheckAsync returns
normally on up-to-date/offline/later, and startup now explicitly restores session
status before running post-update resource verification.

All newly installed package versions (including the old helper's --update-ack
startup path) automatically call the existing managed-resource verifier once per
informational version and normalized game installation path. Successful receipts
are written atomically only after InstallResources finishes. Failures leave the
check pending and Launch disabled; Settings verification can retry. A running
KOTOR II defers the check and the 3-second refresh retries after it closes. No
additional arbitrary file deletion or global Steam-file replacement is added.
The existing backup/catalog merge/foreign 101PER.mod checks remain in force.
The updater's startup acknowledgement still indicates launcher startup; it is not
misrepresented as proof of successful game-resource verification. A missing receipt
causes unfinished verification to be retried on a later start.

Corrected SendMessage's P/Invoke argument from Control to IntPtr/Handle. Header,
labels and logo keep their drag handlers. Position persists in launcher-window.json.
Placement chooses the saved/cursor monitor, handles negative coordinates, and
clamps the reachable window into a current work area. Removed-monitor placement
falls back to a visible screen. This is separate from game display settings.

Validation:
- Launcher publish succeeded without compiler errors/warnings.
- 14 maintenance checks passed (version/path receipt, malformed receipt, monitor
  geometry including negative coordinates/disconnected monitor, saved position).
- Launched the actual full 0.3.27 package in Windows. It automatically verified
  1350 managed game files, restored 0, and showed Game files verified. Ready to launch.
- Dragged header from window origin (270,136) to (370,196), a real +100/+60 move.
- Closed and reopened. Origin restored to (370,196); status was the ordinary
  Launch the game to begin your co-op session, not the checking-updates text.
- No game was launched for these launcher checks. The runtime payload remains
  the previously built 0.3.26 animation repair.

Evidence: logs/launcher-maintenance-checks.log; post-update verifier entry in
LocalAppData/KOTOR2_Coop/Updates/updater.log; output/launcher/0.3.27-after-verification.png.
