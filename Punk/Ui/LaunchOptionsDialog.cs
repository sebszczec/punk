using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Punk.Ui;

/// <summary>Modal editor for one game's extra launch arguments. Returns the new text, "" to clear, or null when cancelled.</summary>
public static class LaunchOptionsDialog
{
    public static string? Show(IApplication app, string gameName, string current, Scheme scheme)
    {
        bool saved = false;

        Dialog dialog = new() { Title = "Launch options", Width = 70, Height = 11 };
        dialog.SetScheme(scheme);

        Label name = new() { X = 1, Y = 1, Width = Dim.Fill(1), Text = gameName };
        Label label = new() { X = 1, Y = 3, Text = "Extra arguments" };
        TextField args = new() { X = 1, Y = 4, Width = Dim.Fill(1), Text = current };
        Label hint = new() { X = 1, Y = 6, Width = Dim.Fill(1), Text = "Passed to the game for this launch only, e.g. --launcher-skip" };

        dialog.Add(name, label, args, hint);

        Button save = new() { Text = "Save", IsDefault = true };
        Button cancel = new() { Text = "Cancel" };

        save.Accepting += (_, _) => saved = true; // not handled: the dialog closes

        dialog.AddButton(save);
        dialog.AddButton(cancel);

        app.Run(dialog);
        dialog.Dispose();

        return saved ? args.Text.Trim() : null;
    }
}
