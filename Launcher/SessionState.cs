using System.Globalization;

namespace ForceBond.Launcher;

internal sealed record SessionState(Dictionary<string, string> Fields)
{
    public bool Flag(string key) => Fields.Int(key) == 1;
    public string Text(string key) => Fields.GetValueOrDefault(key, "");
    public static string FilePath(int pid) => Path.Combine(LauncherSettings.DataDirectory, $"session-{pid}.ini");

    public static SessionState? Parse(string text, int pid, long processStart, DateTime nowUtc)
    {
        var fields = IniValues.Parse(text);
        if (fields.Int("TelemetryVersion") != 1 || fields.Int("PID") != pid ||
            !long.TryParse(fields.GetValueOrDefault("ProcessStart"), NumberStyles.None, CultureInfo.InvariantCulture, out long start) ||
            start != processStart || !long.TryParse(fields.GetValueOrDefault("Heartbeat"), NumberStyles.None, CultureInfo.InvariantCulture, out long heartbeat)) return null;
        try
        {
            double age = (nowUtc - DateTime.FromFileTimeUtc(heartbeat)).TotalSeconds;
            if (age is < -2 or > 6) return null;
        }
        catch (ArgumentOutOfRangeException) { return null; }
        return new SessionState(fields);
    }

    public static SessionState? Read(int pid, long processStart)
    {
        try
        {
            using var stream = new FileStream(FilePath(pid), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd(), pid, processStart, DateTime.UtcNow);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

internal sealed record SessionView(bool[] Steps, string Description, string RuntimeText, bool Warning)
{
    public static SessionView Create(bool gameRunning, bool moduleLoaded, SessionState? state)
    {
        if (state?.Text("RuntimeEngine") == "OdysseyMP") {
            if (state.Flag("Prologue")) return new([gameRunning, gameRunning, false, false, false], "Finish or skip the prologue to begin co-op.", "PROLOGUE / CO-OP WAITING", false);
            string error = state.Text("SteamError");
            if (!string.IsNullOrEmpty(error)) return new([gameRunning, gameRunning, gameRunning && state.Flag("WorldLoaded"), false, false], error, "STEAM SESSION ERROR", true);
            bool worldReady = gameRunning && state.Flag("WorldLoaded");
            bool sessionReady = worldReady && state.Flag("SessionReady");
            bool players = sessionReady && state.Flag("PlayersInWorld");
            return new([gameRunning, gameRunning, worldReady, sessionReady, players],
                !worldReady ? "Load a character to begin a Steam session." : !sessionReady ? "Creating Steam session..." : !players ? "World ready. Invite friends through Steam (F8)." : "Players present in the host's world.",
                !worldReady ? "ODYSSEYMP / MAIN MENU" : !sessionReady ? "CREATING STEAM SESSION" : !players ? "STEAM SESSION READY" : "PLAYERS IN WORLD", false);
        }
        bool loaded = gameRunning && (moduleLoaded || state is not null);
        bool world = loaded && state is not null && state.Flag("WorldLoaded");
        bool ready = world && state!.Flag("BridgeReady") && state.Flag("ProfileReady");
        bool partner = loaded && state is not null && state.Flag("PeerConnected") && state.Flag("HandshakeReady");
        bool poseReady = state is not null && (state.Text("PoseDriver") != "NATIVE_DIRECT" || state.Flag("PoseLive"));
        bool sync = ready && partner && state!.Flag("MirrorReady") && state.Flag("RemoteStateFresh") && poseReady;
        bool[] steps = [gameRunning, loaded, ready, ready && partner, sync];
        if (!gameRunning) return new(steps, "Launch the game to begin your co-op session.", "READY TO LAUNCH", false);
        if (!loaded) return new(steps, "Game running. Connect Force Bond to enable co-op.", "GAME OPEN / MOD NOT LOADED", true);
        if (state is null) return new(steps, "Runtime loaded; live status unavailable. Restart with this build if needed.", "LIVE STATUS UNAVAILABLE", true);
        if (!state.Flag("OverlayReady")) return new(steps, "The in-game overlay could not load. Open Diagnostics.", "OVERLAY UNAVAILABLE", true);
        if (!world) return new(steps, "Load a save on Peragus - Administration Level.", "RUNTIME LOADED / MAIN MENU", false);
        if (!state.Flag("BridgeReady")) return new(steps, "World loaded; co-op initialization is pending. Open Diagnostics if it persists.", "INITIALIZING CO-OP WORLD", true);
        if (!state.Flag("ProfileReady")) return new(steps, "World ready; waiting for your character's appearance.", "READING CHARACTER PROFILE", true);
        if (!partner) return new(steps, "World ready. Invite or join a friend through Steam Friends.", "WORLD READY / WAITING FOR PARTNER", false);
        if (!poseReady && state.Flag("MirrorReady")) return new(steps, "Native movement driver is not bound. Open Diagnostics.", "PARTNER CONNECTED / SYNC PENDING", true);
        if (!sync) return new(steps, "Partner connected; waiting for the NPC and movement updates.", "PARTNER CONNECTED / SYNC PENDING", true);
        return new(steps, "Partner NPC present and fresh movement updates received.", "MOVEMENT LINK ACTIVE", false);
    }
}
