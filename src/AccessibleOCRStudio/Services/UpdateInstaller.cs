using System.Diagnostics;
using Microsoft.Win32;

namespace AccessibleOCRStudio.Services;

internal static class UpdateInstaller
{
    internal static ProcessStartInfo CreateStartInfo(string installer, string directory, bool currentUser)
    {
        var start = new ProcessStartInfo(installer) { UseShellExecute = true };
        if (!currentUser) start.Verb = "runas";
        start.ArgumentList.Add("/SILENT");
        start.ArgumentList.Add("/NORESTART");
        start.ArgumentList.Add("/NOCLOSEAPPLICATIONS");
        start.ArgumentList.Add(currentUser ? "/CURRENTUSER" : "/ALLUSERS");
        start.ArgumentList.Add("/DIR=" + Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar));
        return start;
    }

    internal static bool IsCurrentUserInstall(string directory)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A9C570A-BA44-49B6-95E4-73B724DD0A60}_is1");
        var installed = key?.GetValue("InstallLocation") as string;
        return !string.IsNullOrWhiteSpace(installed) &&
            string.Equals(Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
