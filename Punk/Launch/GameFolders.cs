using Punk.Steam;

namespace Punk.Launch;

public static class GameFolders
{
    /// <summary>The folder the game's files (and running processes) live in, for any launcher.</summary>
    public static string InstallFolder(Game g) => g.Source switch
    {
        GameSource.Steam => Path.Combine(g.LibraryPath, "steamapps", "common", g.InstallDir),
        _ => g.InstallDir,
    };
}
