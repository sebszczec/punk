using Microsoft.Data.Sqlite;

namespace Punk.Gog;

/// <summary>
/// Reads last-played time and playtime from Galaxy's local database (galaxy-2.0.db), keyed by "gog_&lt;gameID&gt;".
/// Galaxy writes in WAL mode, so the file is opened read-only and never modified; any failure just yields no stats.
/// </summary>
public static class GogPlayStats
{
    public static Dictionary<string, (DateTimeOffset? LastPlayed, TimeSpan Playtime)> Read()
    {
        Dictionary<string, (DateTimeOffset?, TimeSpan)> result = [];
        string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");
        if (!File.Exists(db)) return result;

        try
        {
            using SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString());
            c.Open();

            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT gameReleaseKey, lastPlayedDate FROM LastPlayedDates";
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    // "2026-10-12 12:35:47", UTC
                    if (!DateTime.TryParse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime t)) continue;
                    DateTimeOffset when = new(t, TimeSpan.Zero);
                    if (!result.TryGetValue(r.GetString(0), out var cur) || cur.Item1 is null || when > cur.Item1) result[r.GetString(0)] = (when, cur.Item2);
                }
            }

            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT releaseKey, minutesInGame FROM GameTimes";
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.TryGetValue(r.GetString(0), out var cur);
                    result[r.GetString(0)] = (cur.Item1, cur.Item2 + TimeSpan.FromMinutes(r.GetInt64(1)));
                }
            }
        }
        catch (Exception) { /* locked or unfamiliar schema: fall back to no stats */ }
        return result;
    }
}
