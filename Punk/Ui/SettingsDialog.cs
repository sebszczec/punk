using Punk.Config;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Punk.Ui;

/// <summary>Modal settings editor. Returns the saved config, or null when cancelled.</summary>
public static class SettingsDialog
{
    public static AppConfig? Show(IApplication app, AppConfig current, Scheme scheme)
    {
        AppConfig edit = current.Clone();
        bool saved = false;
        string? error = null;

        Dialog dialog = new() { Title = "Settings", Width = 70, Height = 25 };
        dialog.SetScheme(scheme);

        Label L(string text, int y) => new() { Text = text, X = 1, Y = y };
        const int fx = 26;

        TextField interval = new() { X = fx, Y = 1, Width = 8, Text = edit.RefreshIntervalSeconds.ToString() };
        TextField steamPath = new() { X = fx, Y = 3, Width = Dim.Fill(2), Text = edit.SteamPathOverride };
        TextField extra = new() { X = fx, Y = 5, Width = Dim.Fill(2), Text = string.Join(';', edit.ExtraLibraryPaths) };
        TextField bnetRoots = new() { X = fx, Y = 7, Width = Dim.Fill(2), Text = string.Join(';', edit.BattleNetExtraRoots) };
        TextField accent = new() { X = fx, Y = 9, Width = 10, Text = edit.AccentColor };
        Label swatch = new() { X = fx + 12, Y = 9, Text = "████" };
        CheckBox hideTools = new() { X = 1, Y = 11, Text = "Hide Steam tools (Proton, redistributables)", Value = edit.HideSteamTools ? CheckState.Checked : CheckState.UnChecked };
        CheckBox bnet = new() { X = 1, Y = 13, Text = "Include Battle.net games (Windows)", Value = edit.BattleNetEnabled ? CheckState.Checked : CheckState.UnChecked };
        CheckBox ascii = new() { X = 1, Y = 12, Text = "Use plain ASCII symbols", Value = edit.UseAsciiSymbols ? CheckState.Checked : CheckState.UnChecked };
        CheckBox gog = new() { X = 1, Y = 14, Text = "Include GOG Galaxy games (Windows)", Value = edit.GogEnabled ? CheckState.Checked : CheckState.UnChecked };
        Button sort = new() { X = fx, Y = 16, Text = $"{edit.SortBy}", NoDecorations = false };
        Label hint = new() { X = 1, Y = 18, Width = Dim.Fill(1), Text = $"Config: {ConfigStore.Path}" };
        Label err = new() { X = 1, Y = 19, Width = Dim.Fill(1), Text = "" };

        void UpdateSwatch()
        {
            if (AppConfig.TryParseColor(accent.Text, out var rgb))
                swatch.SetScheme(Theme.Colored(new Color(rgb.R, rgb.G, rgb.B)));
        }
        UpdateSwatch();
        accent.ValueChanged += (_, _) => UpdateSwatch();

        sort.Accepting += (s, e) =>
        {
            edit.SortBy = (SortMode)(((int)edit.SortBy + 1) % Enum.GetValues<SortMode>().Length);
            sort.Text = $"{edit.SortBy}";
            e.Handled = true;
        };

        dialog.Add(
            L("Refresh every (sec)", 1), interval,
            L("Steam path override", 3), steamPath,
            L("Extra libraries (;)", 5), extra,
            L("Battle.net roots (;)", 7), bnetRoots,
            L("Accent color (#RRGGBB)", 9), accent, swatch,
            hideTools, ascii, bnet, gog,
            L("Sort by (Enter cycles)", 16), sort,
            hint, err);

        Button save = new() { Text = "Save", IsDefault = true };
        Button cancel = new() { Text = "Cancel" };
        Button reset = new() { Text = "Reset defaults" };

        save.Accepting += (s, e) =>
        {
            if (!int.TryParse(interval.Text, out int secs) || secs < 0 || secs is > 0 and < AppConfig.MinRefreshSeconds)
                error = $"Refresh interval must be 0 (off) or at least {AppConfig.MinRefreshSeconds} seconds.";
            else if (!AppConfig.TryParseColor(accent.Text, out _))
                error = "Accent color must look like #66C0F4.";
            else if (steamPath.Text.Trim().Length > 0 && !Directory.Exists(Path.Combine(steamPath.Text.Trim(), "steamapps")))
                error = "Steam path must contain a 'steamapps' folder.";
            else
                error = null;

            if (error is not null)
            {
                err.SetScheme(Theme.Colored(Theme.Danger));
                err.Text = error;
                e.Handled = true; // keep the dialog open
                return;
            }

            edit.RefreshIntervalSeconds = secs;
            edit.SteamPathOverride = steamPath.Text.Trim();
            edit.ExtraLibraryPaths = [.. extra.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            edit.BattleNetExtraRoots = [.. bnetRoots.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            edit.BattleNetEnabled = bnet.Value == CheckState.Checked;
            edit.GogEnabled = gog.Value == CheckState.Checked;
            edit.AccentColor = accent.Text.Trim();
            edit.HideSteamTools = hideTools.Value == CheckState.Checked;
            edit.UseAsciiSymbols = ascii.Value == CheckState.Checked;
            saved = true;
        };

        reset.Accepting += (s, e) =>
        {
            AppConfig d = new();
            interval.Text = d.RefreshIntervalSeconds.ToString();
            steamPath.Text = d.SteamPathOverride;
            extra.Text = "";
            bnetRoots.Text = "";
            bnet.Value = CheckState.Checked;
            gog.Value = CheckState.Checked;
            accent.Text = d.AccentColor;
            hideTools.Value = CheckState.Checked;
            ascii.Value = CheckState.UnChecked;
            edit.SortBy = d.SortBy;
            sort.Text = $"{d.SortBy}";
            e.Handled = true;
        };

        dialog.AddButton(save);
        dialog.AddButton(reset);
        dialog.AddButton(cancel);

        app.Run(dialog);
        dialog.Dispose();

        if (!saved) return null;
        edit.Normalize();
        try { ConfigStore.Save(edit); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* settings still apply for this session */ }
        return edit;
    }
}
