using Punk.Steam;

namespace Punk.Launch;

public static class GameFolders
{
    /// <summary>The folder the game's files (and running processes) live in, for any launcher.</summary>
    public static string InstallFolder(Game g) => g.Source switch
    {
        // Empty installdir would resolve to the shared "common" folder and make every Steam game look running.
        GameSource.Steam => string.IsNullOrWhiteSpace(g.InstallDir) ? "" : Path.Combine(g.LibraryPath, "steamapps", "common", g.InstallDir),
        _ => g.InstallDir,
    };
}
