using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RemotePlayLauncher;

public static class ShortcutResolver
{
    public static CustomLaunchProfile Import(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("The selected shortcut or executable does not exist.", sourcePath);

        sourcePath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        return extension switch
        {
            ".lnk" => ImportShellLink(sourcePath),
            ".url" => ImportInternetShortcut(sourcePath),
            ".exe" or ".bat" or ".cmd" or ".com" => ImportExecutable(sourcePath),
            _ => throw new NotSupportedException($"Unsupported file type: {extension}")
        };
    }

    public static CustomLaunchProfile Refresh(CustomLaunchProfile profile)
    {
        if (profile.SourceKind != LaunchSourceKind.Shortcut || !File.Exists(profile.SourcePath))
            return profile;

        var refreshed = ImportShellLink(profile.SourcePath);
        refreshed.Id = profile.Id;
        refreshed.Name = profile.Name;
        refreshed.RunAsAdministrator = profile.RunAsAdministrator;
        return refreshed;
    }

    private static CustomLaunchProfile ImportExecutable(string sourcePath)
    {
        return new CustomLaunchProfile
        {
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            SourceKind = LaunchSourceKind.Executable,
            SourcePath = sourcePath,
            ExecutablePath = sourcePath,
            WorkingDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty,
            IconPath = sourcePath,
            RunAsAdministrator = false
        };
    }

    private static CustomLaunchProfile ImportInternetShortcut(string sourcePath)
    {
        var url = File.ReadLines(sourcePath)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..]
            .Trim();

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new InvalidDataException("The .url file does not contain a valid URL.");

        return new CustomLaunchProfile
        {
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            SourceKind = LaunchSourceKind.Url,
            SourcePath = sourcePath,
            ExecutablePath = url,
            IconPath = sourcePath,
            RunAsAdministrator = false
        };
    }

    private static CustomLaunchProfile ImportShellLink(string sourcePath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows Script Host could not be created.");
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(sourcePath);
            dynamic link = shortcut;

            var target = NormalizePath((string?)link.TargetPath, Path.GetDirectoryName(sourcePath));
            var arguments = ((string?)link.Arguments ?? string.Empty).Trim();
            var workingDirectory = NormalizePath((string?)link.WorkingDirectory, Path.GetDirectoryName(target));
            var (iconPath, iconIndex) = ParseIconLocation((string?)link.IconLocation, target);

            // Some advertised, packaged or UWP shortcuts intentionally expose no target.
            // ShellExecute can still launch the original .lnk, but elevation may not apply.
            if (string.IsNullOrWhiteSpace(target))
                target = sourcePath;

            return new CustomLaunchProfile
            {
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                SourceKind = LaunchSourceKind.Shortcut,
                SourcePath = sourcePath,
                ExecutablePath = target,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                IconPath = iconPath,
                IconIndex = iconIndex,
                // Requested behavior for imported desktop shortcuts. The UI exposes
                // a per-entry switch because elevated apps may reject Remote Play input.
                RunAsAdministrator = !string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase)
            };
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string NormalizePath(string? value, string? relativeTo)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (!Path.IsPathRooted(value) && !string.IsNullOrWhiteSpace(relativeTo))
            value = Path.Combine(relativeTo, value);
        try { return Path.GetFullPath(value); }
        catch { return value; }
    }

    private static (string Path, int Index) ParseIconLocation(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return (fallback, 0);
        value = Environment.ExpandEnvironmentVariables(value.Trim());
        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), out var index))
            return (NormalizePath(value[..comma], Path.GetDirectoryName(fallback)), index);
        return (NormalizePath(value, Path.GetDirectoryName(fallback)), 0);
    }
}

public static class LaunchService
{
    public static Process? Start(CustomLaunchProfile originalProfile)
    {
        var profile = ShortcutResolver.Refresh(originalProfile);
        var path = profile.ExecutablePath;
        var isUri = Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;

        if (!isUri && !File.Exists(path))
            throw new FileNotFoundException("The configured target no longer exists.", path);

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = Directory.Exists(profile.WorkingDirectory)
                ? profile.WorkingDirectory
                : (isUri ? string.Empty : Path.GetDirectoryName(path) ?? string.Empty)
        };

        if (profile.RunAsAdministrator && !isUri)
            startInfo.Verb = "runas";

        try
        {
            return Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("The administrator request was cancelled.", ex);
        }
    }

    /// <summary>
    /// Starts an unelevated executable directly from the donor process. Avoiding
    /// ShellExecute here is intentional: Steam can then associate the new child
    /// with the donor AppID and keep Remote Play Together input enabled.
    /// </summary>
    public static Process StartTracked(CustomLaunchProfile originalProfile)
    {
        var profile = ShortcutResolver.Refresh(originalProfile);
        var path = profile.ExecutablePath;
        if (!File.Exists(path))
            throw new FileNotFoundException("The configured target no longer exists.", path);
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only direct .exe applications can be restarted inside Steam.");
        if (profile.RunAsAdministrator)
            throw new InvalidOperationException("Disable administrator mode before restarting this application inside Steam.");

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = Directory.Exists(profile.WorkingDirectory)
                ? profile.WorkingDirectory
                : Path.GetDirectoryName(path) ?? string.Empty
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the selected application.");
    }
}
