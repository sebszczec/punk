using System.Text.Json;

namespace Punk.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Path { get; } = ResolvePath();

    private static string ResolvePath()
    {
        string? dir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        if (string.IsNullOrEmpty(dir))
            dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return System.IO.Path.Combine(dir, OperatingSystem.IsWindows() ? "Punk" : "punk", "config.json");
    }

    /// <summary>Loads the config; never throws. <paramref name="warning"/> is set when defaults were used because of a problem.</summary>
    public static AppConfig Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(Path))
            {
                AppConfig fresh = new();
                Save(fresh);
                return fresh;
            }

            AppConfig config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path), Options) ?? new();
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            warning = $"Could not read config ({ex.Message}); using defaults.";
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, Path, overwrite: true);
    }
}
