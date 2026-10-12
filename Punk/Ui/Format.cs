namespace Punk.Ui;

public static class Format
{
    public static string Ago(DateTimeOffset? when)
    {
        if (when is null) return "never";
        TimeSpan d = DateTimeOffset.UtcNow - when.Value;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays}d ago";
        if (d.TotalDays < 365) return $"{(int)(d.TotalDays / 30)}mo ago";
        return $"{(int)(d.TotalDays / 365)}y ago";
    }

    public static string Playtime(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "-";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m";
        return t.TotalHours < 100 ? $"{t.TotalHours:F1}h" : $"{(int)t.TotalHours}h";
    }

    /// <summary>Elapsed time of a live session: 45s, 12m, 1h 05m.</summary>
    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{Math.Max(0, (int)t.TotalSeconds)}s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m";
        return $"{(int)t.TotalHours}h {t.Minutes:00}m";
    }

    public static string Size(long bytes)
    {
        if (bytes <= 0) return "-";
        double gb = bytes / 1_073_741_824.0;
        if (gb >= 1) return $"{gb:F1} GB";
        return $"{bytes / 1_048_576.0:F0} MB";
    }

    public static string Fit(string text, int width)
    {
        if (width <= 0) return "";
        if (text.Length <= width) return text.PadRight(width);
        return width == 1 ? "…" : text[..(width - 1)] + "…";
    }
}
