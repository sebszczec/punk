using System.Reflection;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Punk.Config;

namespace Punk.Update;

/// <summary>A downloaded, verified update waiting to be installed on the next start.</summary>
public sealed record ReadyMarker(string Version);

public static class UpdatePaths
{
    public static string Root { get; } = Path.Combine(Path.GetDirectoryName(ConfigStore.Path)!, "updates");
    public static string MarkerFile { get; } = Path.Combine(Root, "ready.json");
    public static string FilesDir(string version) => Path.Combine(Root, version, "files");
}

/// <summary>Checks GitHub Releases for a newer Punk and stages it locally. Installing happens in <see cref="UpdateApplier"/>.</summary>
public static class Updater
{
    private const string Repo = "sebszczec/punk";
    private const string ZipName = "punk-win-x64.zip";
    private const string LinuxZipName = "punk-linux-x64.zip";
    private const string SumsName = "SHA256SUMS";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Running version, normalized to major.minor.build.</summary>
    public static Version Current { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Updates only apply to a published build. Under <c>dotnet run</c> the process is the dotnet host, not Punk,
    /// so this must not touch that folder. Disabled by the setting, <c>PUNK_NO_UPDATE=1</c> or the self-test.
    /// </summary>
    public static bool IsEnabled(AppConfig config) =>
        config.CheckForUpdates
        && Environment.GetEnvironmentVariable("PUNK_NO_UPDATE") != "1"
        && Environment.GetEnvironmentVariable("PUNK_SELFTEST") is null
        && Environment.ProcessPath is { } exe
        && string.Equals(Path.GetFileNameWithoutExtension(exe), "Punk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Version waiting to be installed, if any.</summary>
    public static string? PendingVersion() => ReadReady()?.Version;

    public static ReadyMarker? ReadReady()
    {
        try
        {
            return File.Exists(UpdatePaths.MarkerFile)
                ? JsonSerializer.Deserialize<ReadyMarker>(File.ReadAllText(UpdatePaths.MarkerFile))
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Removes the staged update (marker and files).</summary>
    public static void ClearReady() => TryDeleteDirectory(UpdatePaths.Root);

    /// <summary>
    /// Asks GitHub for the latest release and, when it is newer, downloads and verifies it.
    /// Returns the staged version, or null when there is nothing new or the check failed. Never throws.
    /// </summary>
    public static async Task<string?> CheckAndStageAsync()
    {
        try
        {
            string url = Environment.GetEnvironmentVariable("PUNK_UPDATE_URL")
                ?? $"https://api.github.com/repos/{Repo}/releases/latest";

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url, timeout.Token));
            JsonElement root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? parsed)) return null;
            Version latest = Normalize(parsed);
            if (latest <= Current) return null;

            string version = latest.ToString(3);
            if (PendingVersion() == version) return version; // already downloaded

            string zipName = OperatingSystem.IsWindows() ? ZipName : LinuxZipName;
            JsonElement assets = root.GetProperty("assets");
            string? zipUrl = FindAssetUrl(assets, zipName);
            string? sumsUrl = FindAssetUrl(assets, SumsName);
            if (zipUrl is null || sumsUrl is null) return null;

            await StageAsync(version, zipName, zipUrl, sumsUrl);
            return version;
        }
        catch
        {
            return null; // offline, rate limited, bad checksum...: try again at the next check
        }
    }

    private static async Task StageAsync(string version, string zipName, string zipUrl, string sumsUrl)
    {
        string dir = Path.Combine(UpdatePaths.Root, version);
        TryDeleteDirectory(dir);
        Directory.CreateDirectory(dir);

        string zipPath = Path.Combine(dir, zipName);
        await using (Stream source = await Http.GetStreamAsync(zipUrl))
        await using (FileStream target = File.Create(zipPath))
            await source.CopyToAsync(target);

        string sums = await Http.GetStringAsync(sumsUrl);
        string expected = ExpectedHash(sums, zipName) ?? throw new InvalidDataException($"{zipName} is not listed in {SumsName}.");
        string actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath))).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch for {zipName}.");

        ZipFile.ExtractToDirectory(zipPath, UpdatePaths.FilesDir(version), overwriteFiles: true);
        File.Delete(zipPath);

        // Drop older staged versions, then publish the marker last: a half-finished download is never applied.
        foreach (string other in Directory.EnumerateDirectories(UpdatePaths.Root))
            if (!string.Equals(other, dir, StringComparison.OrdinalIgnoreCase)) TryDeleteDirectory(other);

        string tmp = UpdatePaths.MarkerFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new ReadyMarker(version)));
        File.Move(tmp, UpdatePaths.MarkerFile, overwrite: true);
    }

    private static string? FindAssetUrl(JsonElement assets, string name)
    {
        foreach (JsonElement asset in assets.EnumerateArray())
            if (asset.GetProperty("name").GetString() == name)
                return asset.GetProperty("browser_download_url").GetString();
        return null;
    }

    /// <summary>Parses "hash  filename" lines (sha256sum format).</summary>
    private static string? ExpectedHash(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName) return parts[0];
        }
        return null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Punk-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* locked or already gone */ }
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* locked: removed at a later start */ }
    }
}
