using System.Globalization;
using System.Text.Json.Serialization;

namespace Punk.Config;

[JsonConverter(typeof(JsonStringEnumConverter<SortMode>))]
public enum SortMode { LastPlayed, Name, Playtime, Size }

[JsonConverter(typeof(JsonStringEnumConverter<UiPreset>))]
public enum UiPreset { Default, Cyberpunk, Colorful, Ambient }

public sealed class AppConfig
{
    public const int MinRefreshSeconds = 5;

    /// <summary>Seconds between automatic rescans. 0 disables auto refresh.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    /// <summary>Steam install directory; empty = auto-detect.</summary>
    public string SteamPathOverride { get; set; } = "";

    /// <summary>Additional library folders (each containing a steamapps directory).</summary>
    public List<string> ExtraLibraryPaths { get; set; } = [];

    public bool HideSteamTools { get; set; } = true;

    /// <summary>Include installed Battle.net games (Windows only).</summary>
    public bool BattleNetEnabled { get; set; } = true;

    /// <summary>Extra folders that contain Battle.net game folders (besides the client's default install path).</summary>
    public List<string> BattleNetExtraRoots { get; set; } = [];

    /// <summary>Include installed GOG Galaxy games (Windows only).</summary>
    public bool GogEnabled { get; set; } = true;

    /// <summary>Accent color as #RRGGBB.</summary>
    public string AccentColor { get; set; } = "#66C0F4";

    /// <summary>Visual preset; F4 cycles through them. Only Default uses <see cref="AccentColor"/>.</summary>
    public UiPreset Preset { get; set; } = UiPreset.Default;

    public bool UseAsciiSymbols { get; set; }

    public SortMode SortBy { get; set; } = SortMode.LastPlayed;

    /// <summary>Clamps values into their valid ranges.</summary>
    public void Normalize()
    {
        if (RefreshIntervalSeconds < 0) RefreshIntervalSeconds = 0;
        else if (RefreshIntervalSeconds is > 0 and < MinRefreshSeconds) RefreshIntervalSeconds = MinRefreshSeconds;
        SteamPathOverride ??= "";
        ExtraLibraryPaths ??= [];
        BattleNetExtraRoots ??= [];
        if (!TryParseColor(AccentColor, out _)) AccentColor = new AppConfig().AccentColor;
    }

    public static bool TryParseColor(string? text, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (text is not { Length: 7 } || text[0] != '#') return false;
        if (!int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v)) return false;
        rgb = ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public AppConfig Clone() => new()
    {
        RefreshIntervalSeconds = RefreshIntervalSeconds,
        SteamPathOverride = SteamPathOverride,
        ExtraLibraryPaths = [.. ExtraLibraryPaths],
        HideSteamTools = HideSteamTools,
        BattleNetEnabled = BattleNetEnabled,
        BattleNetExtraRoots = [.. BattleNetExtraRoots],
        GogEnabled = GogEnabled,
        AccentColor = AccentColor,
        Preset = Preset,
        UseAsciiSymbols = UseAsciiSymbols,
        SortBy = SortBy,
    };
}
