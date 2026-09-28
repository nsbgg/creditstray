using Microsoft.Win32;
using System.IO;

namespace CreditsTray;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "CreditsTray";

    public static bool IsSupported => GetLaunchCommand() is not null;

    public static bool IsEnabled
    {
        get
        {
            var launchCommand = GetLaunchCommand();
            if (launchCommand is null)
            {
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return string.Equals(key?.GetValue(AppValueName) as string, launchCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        var launchCommand = GetLaunchCommand();
        if (launchCommand is null)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key?.SetValue(AppValueName, launchCommand, RegistryValueKind.String);
        }
        else if (key?.GetValue(AppValueName) is not null)
        {
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
        }
    }

    private static string? GetLaunchCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) ||
            !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileNameWithoutExtension(processPath), "CreditsTray", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"\"{processPath}\"";
    }
}
