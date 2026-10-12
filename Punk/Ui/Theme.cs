using Punk.Config;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Punk.Ui;

/// <summary>Palette, color schemes and symbol sets.</summary>
public static class Theme
{
    public static Color Background { get; private set; }
    public static Color Panel { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Success { get; private set; }
    public static Color Warning { get; private set; }
    public static Color Danger { get; private set; }

    /// <summary>Main accent plus two companions; the Colorful preset spreads them over different elements.</summary>
    public static Color Accent { get; private set; }
    public static Color Accent2 { get; private set; }
    public static Color Accent3 { get; private set; }

    public static LineStyle Border { get; private set; } = LineStyle.Rounded;
    public static double SelectionStrength { get; private set; } = 0.28;
    public static UiPreset Current { get; private set; }

    static Theme() => Use(UiPreset.Default, "#66C0F4");

    public static Color AccentColor(string hex)
    {
        AppConfig.TryParseColor(hex, out var c);
        return c == default ? new Color(0x66, 0xC0, 0xF4) : new Color(c.R, c.G, c.B);
    }

    /// <summary>Switches the active palette. Ambient drifts slowly with <paramref name="seconds"/>.</summary>
    public static void Use(UiPreset preset, string accentHex, double seconds = 0)
    {
        Current = preset;
        Border = LineStyle.Rounded;
        SelectionStrength = 0.28;

        switch (preset)
        {
            case UiPreset.Cyberpunk:
                Background = new(0x0A, 0x05, 0x14);
                Panel = new(0x1A, 0x0B, 0x2E);
                Text = new(0xE0, 0xF7, 0xFF);
                Muted = new(0x8A, 0x63, 0xB8);
                Success = new(0x00, 0xFF, 0x9C);
                Warning = new(0xFC, 0xEE, 0x0A);
                Danger = new(0xFF, 0x2A, 0x6D);
                Accent = new(0x05, 0xD9, 0xE8);
                Accent2 = new(0xFF, 0x2A, 0xD4);
                Accent3 = new(0xFC, 0xEE, 0x0A);
                Border = LineStyle.Double;
                SelectionStrength = 0.38;
                break;

            case UiPreset.Colorful:
                Background = new(0x14, 0x13, 0x26);
                Panel = new(0x22, 0x20, 0x3E);
                Text = new(0xF4, 0xF1, 0xFF);
                Muted = new(0x9A, 0x94, 0xC8);
                Success = new(0x4A, 0xDE, 0x80);
                Warning = new(0xFB, 0xBF, 0x24);
                Danger = new(0xFB, 0x71, 0x85);
                Accent = new(0xFF, 0x8A, 0x3D);
                Accent2 = new(0xF4, 0x72, 0xB6);
                Accent3 = new(0x38, 0xBD, 0xF8);
                SelectionStrength = 0.35;
                break;

            case UiPreset.Ambient:
                Background = new(0x10, 0x15, 0x1B);
                Panel = new(0x18, 0x1F, 0x27);
                Text = new(0xB4, 0xC0, 0xC9);
                Muted = new(0x5C, 0x6B, 0x77);
                Success = new(0x8F, 0xB8, 0x9A);
                Warning = new(0xC9, 0xA8, 0x7C);
                Danger = new(0xC4, 0x7A, 0x7A);
                // Hue wanders between teal and lavender over ~2 minutes.
                Accent = Hsv(225 + 55 * Math.Sin(seconds / 20), 0.38, 0.82);
                Accent2 = Hsv(225 + 55 * Math.Sin(seconds / 20 + 2), 0.30, 0.78);
                Accent3 = Hsv(225 + 55 * Math.Sin(seconds / 20 + 4), 0.30, 0.78);
                SelectionStrength = 0.18;
                break;

            default:
                Background = new(0x0E, 0x14, 0x1B);
                Panel = new(0x16, 0x20, 0x2B);
                Text = new(0xC7, 0xD5, 0xE0);
                Muted = new(0x6B, 0x7C, 0x8C);
                Success = new(0xA3, 0xCF, 0x06);
                Warning = new(0xF5, 0xA6, 0x23);
                Danger = new(0xE5, 0x5B, 0x5B);
                Accent = Accent2 = Accent3 = AccentColor(accentHex);
                break;
        }
    }

    private static Color Hsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Color((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    /// <summary>Selection background: the accent blended into the dark background.</summary>
    public static Color Selection(Color accent) => Blend(Background, accent, SelectionStrength);

    public static Color Blend(Color from, Color to, double t) => new(
        (int)(from.R + (to.R - from.R) * t),
        (int)(from.G + (to.G - from.G) * t),
        (int)(from.B + (to.B - from.B) * t));

    /// <summary>A scheme that draws everything in one foreground color on the app background.</summary>
    public static Scheme Colored(Color fg) => new()
    {
        Normal = new Attribute(fg, Background),
        HotNormal = new Attribute(fg, Background),
        Focus = new Attribute(fg, Background),
        HotFocus = new Attribute(fg, Background),
        Disabled = new Attribute(fg, Background),
    };

    public static Scheme CreateScheme()
    {
        Color accent = Accent;
        Color selection = Selection(accent);

        return new Scheme
        {
            Normal = new Attribute(Text, Background),
            HotNormal = new Attribute(accent, Background),
            Focus = new Attribute(Color.White, selection),
            HotFocus = new Attribute(accent, selection),
            Active = new Attribute(Color.White, selection),
            HotActive = new Attribute(accent, selection),
            Highlight = new Attribute(Color.White, selection),
            Editable = new Attribute(Text, Panel),
            ReadOnly = new Attribute(Muted, Panel),
            Disabled = new Attribute(Muted, Background),
        };
    }
}

/// <summary>Glyphs used in the UI, with a plain ASCII fallback.</summary>
public sealed record Symbols(
    string Play, string Added, string Removed, string Refresh, string Search, string Clock,
    string Disk, string Check, string Warn, string Gear, string[] Spinner, string Hot, string Cold)
{
    public static readonly Symbols Unicode = new("▶", "✚", "✖", "⟳", "⌕", "◷", "◼", "✔", "⚠", "⚙",
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"], "●", "○");

    public static readonly Symbols Ascii = new(">", "+", "x", "~", "/", "@", "#", "ok", "!", "*",
        ["|", "/", "-", "\\"], "*", "-");

    public static Symbols For(AppConfig config) =>
        config.UseAsciiSymbols || Environment.GetEnvironmentVariable("PUNK_ASCII") == "1" ? Ascii : Unicode;
}
