using System.Security.Principal;

namespace Punk.Launch;

public static class Elevation
{
    /// <summary>Windows only: CPU sensors (LibreHardwareMonitor via the PawnIO driver) need an administrator terminal.</summary>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using WindowsIdentity id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
