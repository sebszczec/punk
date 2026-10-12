using Punk.BattleNet;
using Punk.Config;
using Punk.Gog;

namespace Punk.Steam;

/// <summary>Scans every supported launcher and merges the results.</summary>
public static class GameScanner
{
    public static Task<ScanResult> ScanAsync(AppConfig config, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(config, progress, ct), ct);

    public static ScanResult Scan(AppConfig config, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        // Steam takes the first 60% of the bar, Battle.net the next 20%, GOG Galaxy the rest.
        IProgress<ScanProgress>? Slice(double from, double span) =>
            progress is null ? null : new Sync(p => progress.Report(new(from + p.Fraction * span, p.Message)));

        ScanResult steam = SteamScanner.Scan(config, Slice(0, 0.6), ct);
        progress?.Report(new(0.6, "Looking for Battle.net…"));
        BattleNetScan b = BattleNetScanner.Scan(config, Slice(0.6, 0.2), ct);
        progress?.Report(new(0.8, "Looking for GOG Galaxy…"));
        GogScan g = GogScanner.Scan(config, Slice(0.8, 0.2), ct);

        IReadOnlyList<string> warnings = [.. (steam.Warnings ?? []), .. b.Warnings, .. g.Warnings];
        IReadOnlyList<string> missing = [.. (steam.MissingPaths ?? []), .. b.MissingPaths, .. g.MissingPaths];
        if (steam.SteamRoot is null && !b.Found && !g.Found)
            return new(null, null, [], "No Steam, Battle.net or GOG Galaxy installation found.", warnings, missing);

        List<Game> all = [.. steam.Games, .. b.Games, .. g.Games];
        progress?.Report(new(1, $"{all.Count} games"));
        return new(steam.SteamRoot, b.Client, SteamScanner.Sort(all, config.SortBy), null, warnings, missing, b.Found, g.Found, g.Client);
    }

    private sealed class Sync(Action<ScanProgress> action) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => action(value);
    }
}
