namespace Punk.Steam;

public enum GameSource { Steam, BattleNet, Gog, Epic }

/// <param name="Id">Steam appid, Battle.net launch code (e.g. "OSI"), GOG game id, or Epic app name.</param>
public sealed record Game(
    string Id,
    GameSource Source,
    string Name,
    string InstallDir,
    long SizeBytes,
    DateTimeOffset? LastPlayed,
    TimeSpan Playtime,
    string LibraryPath)
{
    public string Key => $"{Source}:{Id}";
}
