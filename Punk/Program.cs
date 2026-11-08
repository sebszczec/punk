using Punk.Config;
using Punk.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Punk.Launch;
using Punk.Update;

if (Environment.GetEnvironmentVariable("PUNK_TEMPS_DIAG") is { } diagFile)
{
    TemperatureDiag.Run(diagFile);
    return;
}

// Title shows in the window title bar and the taskbar tooltip. Setting it fails without a console; that is fine.
try { Console.Title = "Punk"; } catch (Exception) { }

AppConfig config = ConfigStore.Load(out string? warning);

// Installs an update downloaded by an earlier run. On success a new process is started and this one exits.
UpdateApplier.Outcome update = UpdateApplier.TryApply(config);
if (update.Relaunched) return;
warning ??= update.Warning;

// PUNK_SELFTEST=<file>: run on the ANSI driver, inject keys, dump the screen and exit (for automated checks).
// PUNK_TEST_LIB=<dir>: with the self-test, a fake Steam library appears in it mid-run to exercise auto refresh.
string? dump = Environment.GetEnvironmentVariable("PUNK_SELFTEST");
string? testLib = Environment.GetEnvironmentVariable("PUNK_TEST_LIB");

using IApplication app = Application.Create().Init(dump is null ? null : "ansi");
app.Keyboard.KeyBindings.Add(Key.Q.WithCtrl, Command.Quit);

using MainWindow window = new(app, config, warning);

if (dump is not null)
{
    int n = 0;
    IInputInjector inj = app.GetInputInjector();
    InputInjectionOptions opts = new();

    void Snap(string label) =>
        File.AppendAllText(dump, $"--- {label} --- focus={window.MostFocused?.GetType().Name}\n{app.Driver?.ToString()}\n");

    // PUNK_SELFTEST_REAL=1: no injection; keys come from the real terminal input (e.g. piped into a pty).
    bool real = Environment.GetEnvironmentVariable("PUNK_SELFTEST_REAL") == "1";
    void Press(Key key) { if (!real) _ = Task.Run(() => inj.InjectKey(key, opts)); }

    File.WriteAllText(dump, "");
    _ = Task.Run(async () => { await Task.Delay(45_000); File.AppendAllText(dump, "--- TIMEOUT ---\n"); Environment.Exit(3); });

    app.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
    {
        n++;
        switch (n)
        {
            case 6: Snap("main"); break;
            case 7: Press(Key.O); break;
            case 8: Press(Key.B); break;
            case 10: Snap("filter ob"); Press(Key.Esc); break;
            case 12: Snap("cleared"); break;
            case 13: Press(Key.CursorDown); break;
            case 15: Snap("down"); break;
            case 16: Press(Key.F2); break;
            case 18: Snap("settings"); break;
            case 19: Press(Key.Esc); break;
            case 21 when testLib is not null && Environment.GetEnvironmentVariable("PUNK_TEST_REMOVE") == "1":
                Directory.Delete(testLib, true); // simulate an unplugged drive / moved library
                break;
            case 21 when testLib is not null:
                Directory.CreateDirectory(Path.Combine(testLib, "steamapps"));
                File.WriteAllText(Path.Combine(testLib, "steamapps", "appmanifest_999999.acf"),
                    "\"AppState\"\n{\n\t\"appid\"\t\"999999\"\n\t\"name\"\t\"Fake Test Game\"\n\t\"StateFlags\"\t\"4\"\n\t\"installdir\"\t\"Fake\"\n\t\"SizeOnDisk\"\t\"123456789\"\n}\n");
                break;
            case 30: Press(Key.F3); break;
            case 32: Snap("sort once"); break; // one F3 must move LastPlayed -> Name, not further (hint-bar double-fire guard)
            case 33: _ = Task.Run(() => inj.InjectMouse(new Mouse { ScreenPosition = new(40, 29), Flags = MouseFlags.LeftButtonClicked }, opts)); break; // hint "Sort": single click must do nothing
            case 35:
                Snap("click sort");
                // Two quick press/release pairs: the driver synthesizes LeftButtonDoubleClicked from them.
                _ = Task.Run(async () =>
                {
                    Mouse at = new() { ScreenPosition = new(40, 29) }; // hint "Sort" (row 29 = hint bar)
                    for (int c = 0; c < 2; c++)
                    {
                        inj.InjectMouse(new Mouse { ScreenPosition = at.ScreenPosition, Flags = MouseFlags.LeftButtonPressed }, opts);
                        inj.InjectMouse(new Mouse { ScreenPosition = at.ScreenPosition, Flags = MouseFlags.LeftButtonReleased }, opts);
                        await Task.Delay(50);
                    }
                });
                break;
            case 37: Snap("after auto refresh"); break;
            case 38: Snap("dclick sort"); app.RequestStop(); return false;
        }
        return true;
    });
}

app.Run(window);
