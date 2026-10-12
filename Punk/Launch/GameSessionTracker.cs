using Punk.Steam;

namespace Punk.Launch;

public enum SessionState { Starting, Running, Exited, NotStarted }

public sealed class GameSession(Game game, string folder)
{
    public Game Game { get; } = game;
    public string Folder { get; } = folder;
    public SessionState State { get; internal set; } = SessionState.Starting;
    public DateTime LaunchedUtc { get; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; internal set; }
    public DateTime? EndedUtc { get; internal set; }
    internal DateTime LastSeenUtc { get; set; }

    public TimeSpan Played => StartedUtc is { } s ? (EndedUtc ?? DateTime.UtcNow) - s : TimeSpan.Zero;
}

/// <summary>
/// Follows launched games through Starting -> Running -> Exited (or NotStarted), for every launcher.
/// <see cref="Changed"/> is raised on a background thread.
/// </summary>
public sealed class GameSessionTracker : IDisposable
{
    // Launchers may update/log in first (Battle.net cold start alone can take ~2 min), so be patient.
    public static TimeSpan StartTimeout { get; set; } = TimeSpan.FromMinutes(5);
    // Games often restart themselves or hand over between processes (launcher -> game exe): debounce the exit.
    public static TimeSpan ExitGrace { get; set; } = TimeSpan.FromSeconds(6);
    public static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, GameSession> _active = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string, bool> _isRunning;

    public event Action<GameSession>? Changed;

    public GameSessionTracker(Func<string, bool>? isRunning = null)
    {
        _isRunning = isRunning ?? GameProcesses.IsRunning;
        _ = Task.Run(LoopAsync);
    }

    /// <summary>Starts following a game that was just launched (replaces any session for the same game).</summary>
    public GameSession Track(Game game)
    {
        GameSession session = new(game, GameFolders.InstallFolder(game));
        lock (_active) _active[game.Key] = session;
        Changed?.Invoke(session);
        return session;
    }

    public IReadOnlyList<GameSession> Active
    {
        get { lock (_active) return [.. _active.Values]; }
    }

    public bool IsRunning(Game game)
    {
        lock (_active) return _active.TryGetValue(game.Key, out GameSession? s) && s.State == SessionState.Running;
    }

    private async Task LoopAsync()
    {
        using PeriodicTimer timer = new(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
                Poll();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One pass over all active sessions. Public for tests.</summary>
    public void Poll()
    {
        DateTime now = DateTime.UtcNow;
        foreach (GameSession s in Active)
        {
            bool running;
            try { running = _isRunning(s.Folder); }
            catch (Exception) { continue; }

            SessionState before = s.State;
            switch (s.State)
            {
                case SessionState.Starting when running:
                    s.State = SessionState.Running;
                    s.StartedUtc = now;
                    s.LastSeenUtc = now;
                    break;
                case SessionState.Starting when now - s.LaunchedUtc > StartTimeout:
                    s.State = SessionState.NotStarted;
                    s.EndedUtc = now;
                    break;
                case SessionState.Running when running:
                    s.LastSeenUtc = now;
                    break;
                case SessionState.Running when now - s.LastSeenUtc > ExitGrace:
                    s.State = SessionState.Exited;
                    s.EndedUtc = s.LastSeenUtc;
                    break;
            }

            if (s.State == before) continue;
            if (s.State is SessionState.Exited or SessionState.NotStarted)
                lock (_active) _active.Remove(s.Game.Key);
            Changed?.Invoke(s);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
