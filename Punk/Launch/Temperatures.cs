using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace Punk.Launch;

/// <summary>CPU / GPU temperatures in °C; null when a sensor is unavailable on this machine.</summary>
public readonly record struct Temps(double? Cpu, double? Gpu);

/// <summary>
/// Best-effort temperature readout without admin rights or drivers.
/// Linux: /sys/class/hwmon (k10temp, coretemp, amdgpu, nouveau...) and nvidia-smi.
/// Windows: LibreHardwareMonitor for the CPU (its driver needs admin rights; otherwise falls back to the
/// "Thermal Zone Information" performance counter) and nvidia-smi for the GPU.
/// </summary>
public static class Temperatures
{
    private static readonly string[] CpuChips = ["k10temp", "coretemp", "zenpower", "cpu_thermal", "x86_pkg_temp"];
    private static readonly string[] GpuChips = ["amdgpu", "nouveau", "radeon"];

    public static Temps Read()
    {
        double? cpu = null, gpu = null;
        try
        {
            if (OperatingSystem.IsLinux()) (cpu, gpu) = ReadHwmon();
            else if (OperatingSystem.IsWindows()) cpu = ReadWindowsCpu();
        }
        catch { /* sensors are optional */ }

        gpu ??= ReadNvidia();
        return new Temps(cpu, gpu);
    }

    private static (double? Cpu, double? Gpu) ReadHwmon()
    {
        double? cpu = null, gpu = null;
        const string root = "/sys/class/hwmon";
        if (!Directory.Exists(root)) return (null, null);

        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string name = ReadText(Path.Combine(dir, "name"))?.Trim() ?? "";
            bool isCpu = CpuChips.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool isGpu = GpuChips.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (!isCpu && !isGpu) continue;

            // Prefer the package/die sensor (Tctl, Tdie, "Package id 0"); otherwise the hottest one.
            double? best = null, preferred = null;
            foreach (string input in Directory.EnumerateFiles(dir, "temp*_input"))
            {
                if (!double.TryParse(ReadText(input), NumberStyles.Float, CultureInfo.InvariantCulture, out double milli)) continue;
                double c = milli / 1000.0;
                if (c is <= 0 or > 150) continue;
                best = best is null ? c : Math.Max(best.Value, c);

                string label = ReadText(input.Replace("_input", "_label"))?.Trim() ?? "";
                if (label is "Tctl" or "Tdie" or "edge" || label.StartsWith("Package", StringComparison.OrdinalIgnoreCase)) preferred = c;
            }

            double? value = preferred ?? best;
            if (value is null) continue;
            if (isCpu) cpu ??= value;
            else gpu ??= value;
        }
        return (cpu, gpu);
    }

    private static Computer? _lhm;
    private static bool _lhmFailed;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static double? ReadWindowsCpu()
    {
        if (_lhmFailed) return ReadWindowsThermalZones();
        try
        {
            if (_lhm is null)
            {
                _lhm = new Computer { IsCpuEnabled = true };
                _lhm.Open();
            }

            double? package = null, hottest = null;
            foreach (IHardware hw in _lhm.Hardware)
            {
                if (hw.HardwareType != HardwareType.Cpu) continue;
                hw.Update();
                foreach (ISensor s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || s.Value is not { } v || v is <= 0 or > 150) continue;
                    hottest = hottest is null ? v : Math.Max(hottest.Value, v);
                    // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)" / "CPU Package".
                    if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                        s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
                        package = v;
                }
            }
            // Without admin rights the ring-0 driver cannot load and no sensors appear.
            return package ?? hottest ?? ReadWindowsThermalZones();
        }
        catch
        {
            _lhmFailed = true;
            try { _lhm?.Close(); } catch { }
            _lhm = null;
            return ReadWindowsThermalZones();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static double? ReadWindowsThermalZones()
    {
        // Fallback, needs no elevation. Values are in Kelvin; the highest zone is the best CPU proxy.
        if (!PerformanceCounterCategory.Exists("Thermal Zone Information")) return null;
        PerformanceCounterCategory cat = new("Thermal Zone Information");
        double? max = null;
        foreach (string instance in cat.GetInstanceNames())
        {
            using PerformanceCounter pc = new("Thermal Zone Information", "Temperature", instance, readOnly: true);
            double c = pc.NextValue() - 273.15;
            if (c is > 0 and < 150) max = max is null ? c : Math.Max(max.Value, c);
        }
        return max;
    }

    private static double? ReadNvidia()
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } return null; }

            double? max = null;
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
                    max = max is null ? c : Math.Max(max.Value, c);
            return max;
        }
        catch { return null; } // nvidia-smi not installed
    }

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }
}

/// <summary>PUNK_TEMPS_DIAG=<file>: dump elevation state and every LibreHardwareMonitor sensor, then exit (for automated checks).</summary>
public static class TemperatureDiag
{
    public static void Run(string file)
    {
        using StringWriter w = new();
        w.WriteLine($"elevated={Elevation.IsElevated()}");
        try
        {
            Computer c = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
            c.Open();
            w.WriteLine($"hardware count={c.Hardware.Count}");
            foreach (IHardware hw in c.Hardware)
            {
                hw.Update();
                w.WriteLine($"{hw.HardwareType}: {hw.Name}");
                foreach (ISensor s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                    w.WriteLine($"  {s.Name} = {s.Value}");
                foreach (IHardware sub in hw.SubHardware)
                {
                    sub.Update();
                    foreach (ISensor s in sub.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                        w.WriteLine($"  [{sub.Name}] {s.Name} = {s.Value}");
                }
            }
            c.Close();
        }
        catch (Exception e) { w.WriteLine($"EXCEPTION {e}"); }
        w.WriteLine($"Temperatures.Read() = {Temperatures.Read()}");
        File.WriteAllText(file, w.ToString());
    }
}
