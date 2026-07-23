using System.IO;
using Microsoft.Win32;

namespace RemotePlayLauncher;

public sealed record AppCompatibilityRepairResult(
    string ExecutablePath,
    string? PreviousUserLayers,
    string? CurrentUserLayers,
    bool UserRuleChanged,
    bool MachineRuleForcesAdministrator)
{
    public static AppCompatibilityRepairResult NotApplicable(string path) =>
        new(path, null, null, false, false);
}

public static class AppCompatibilityService
{
    public const string LayersRegistryPath =
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    /// <summary>
    /// Removes only the per-user RUNASADMIN compatibility token for an executable.
    /// Other compatibility tokens are preserved. Machine-wide rules are detected
    /// but never modified because changing them requires administrative consent.
    /// </summary>
    public static AppCompatibilityRepairResult RepairForcedAdministratorLayer(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return AppCompatibilityRepairResult.NotApplicable(string.Empty);

        var normalizedPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(normalizedPath), ".exe", StringComparison.OrdinalIgnoreCase))
            return AppCompatibilityRepairResult.NotApplicable(normalizedPath);

        string? previousUserLayers = null;
        string? currentUserLayers = null;
        var userRuleChanged = false;

        using (var userLayers = Registry.CurrentUser.OpenSubKey(LayersRegistryPath, writable: true))
        {
            previousUserLayers = userLayers?.GetValue(normalizedPath) as string;
            currentUserLayers = RemoveRunAsAdministratorToken(previousUserLayers);
            userRuleChanged = !string.Equals(
                previousUserLayers,
                currentUserLayers,
                StringComparison.Ordinal);

            if (userLayers != null && userRuleChanged)
            {
                if (string.IsNullOrWhiteSpace(currentUserLayers))
                    userLayers.DeleteValue(normalizedPath, throwOnMissingValue: false);
                else
                    userLayers.SetValue(normalizedPath, currentUserLayers, RegistryValueKind.String);
            }
        }

        return new AppCompatibilityRepairResult(
            normalizedPath,
            previousUserLayers,
            currentUserLayers,
            userRuleChanged,
            MachineRuleForcesAdministrator(normalizedPath));
    }

    public static string? RemoveRunAsAdministratorToken(string? layers)
    {
        if (string.IsNullOrWhiteSpace(layers)) return layers;

        var tokens = layers.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var remaining = tokens
            .Where(token => !string.Equals(token, "RUNASADMIN", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remaining.Length == tokens.Length) return layers;

        // AppCompat may prefix a value with "~" or "^". Those markers have no
        // effect by themselves, so remove the registry value if no real layer remains.
        return remaining.Any(IsMeaningfulLayer)
            ? string.Join(' ', remaining)
            : null;
    }

    private static bool MachineRuleForcesAdministrator(string executablePath)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var layers = localMachine.OpenSubKey(LayersRegistryPath, writable: false);
                if (ContainsRunAsAdministratorToken(layers?.GetValue(executablePath) as string))
                    return true;
            }
            catch
            {
                // A denied or unavailable registry view must not prevent launch.
            }
        }

        return false;
    }

    private static bool ContainsRunAsAdministratorToken(string? layers) =>
        !string.IsNullOrWhiteSpace(layers) &&
        layers.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token, "RUNASADMIN", StringComparison.OrdinalIgnoreCase));

    private static bool IsMeaningfulLayer(string token) =>
        token is not "~" and not "^" and not "#";
}
