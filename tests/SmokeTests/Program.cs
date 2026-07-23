using System.Runtime.InteropServices;
using RemotePlayLauncher;

var directory = Path.Combine(Path.GetTempPath(), "CoopLauncher-SmokeTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);

try
{
    var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    var shortcutPath = Path.Combine(directory, "Unicode juego ñ.lnk");
    object? shell = null;
    object? shortcut = null;
    try
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        shell = Activator.CreateInstance(shellType)!;
        dynamic dynamicShell = shell;
        shortcut = dynamicShell.CreateShortcut(shortcutPath);
        dynamic link = shortcut;
        link.TargetPath = target;
        link.Arguments = "--flag \"hello world\"";
        link.WorkingDirectory = directory;
        link.IconLocation = target + ",0";
        link.Save();
    }
    finally
    {
        if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
        if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
    }

    var imported = ShortcutResolver.Import(shortcutPath);
    Assert(imported.SourceKind == LaunchSourceKind.Shortcut, "shortcut kind");
    Assert(PathsEqual(imported.ExecutablePath, target), "shortcut target");
    Assert(imported.Arguments == "--flag \"hello world\"", "shortcut arguments");
    Assert(PathsEqual(imported.WorkingDirectory, directory), "shortcut working directory");
    Assert(imported.RunAsAdministrator, "shortcut admin default");

    var executable = ShortcutResolver.Import(target);
    Assert(executable.SourceKind == LaunchSourceKind.Executable, "executable kind");
    Assert(!executable.RunAsAdministrator, "executable admin default");

    var compatibilityPath = Path.Combine(directory, "Compatibility Test.exe");
    using (var layers = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
               AppCompatibilityService.LayersRegistryPath, writable: true))
    {
        layers.SetValue(
            compatibilityPath,
            "~ HIGHDPIAWARE RUNASADMIN",
            Microsoft.Win32.RegistryValueKind.String);
    }

    try
    {
        var repaired = AppCompatibilityService.RepairForcedAdministratorLayer(compatibilityPath);
        Assert(repaired.UserRuleChanged, "RUNASADMIN rule detected and repaired");
        Assert(
            string.Equals(repaired.CurrentUserLayers, "~ HIGHDPIAWARE", StringComparison.Ordinal),
            "unrelated compatibility layers preserved");

        using var layers = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            AppCompatibilityService.LayersRegistryPath, writable: false);
        Assert(
            string.Equals(layers?.GetValue(compatibilityPath) as string, "~ HIGHDPIAWARE", StringComparison.Ordinal),
            "repaired compatibility rule persisted");
    }
    finally
    {
        using var layers = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            AppCompatibilityService.LayersRegistryPath, writable: true);
        layers?.DeleteValue(compatibilityPath, throwOnMissingValue: false);
    }

    Assert(
        AppCompatibilityService.RemoveRunAsAdministratorToken("RUNASADMIN") is null,
        "standalone RUNASADMIN rule removed completely");
    Assert(
        AppCompatibilityService.RemoveRunAsAdministratorToken("WIN7RTM") == "WIN7RTM",
        "non-admin compatibility rule unchanged");

    var compatibilityLaunchPath = Path.Combine(directory, "Compatibility Launch.exe");
    File.Copy(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        compatibilityLaunchPath);
    using (var layers = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
               AppCompatibilityService.LayersRegistryPath, writable: true))
    {
        layers.SetValue(
            compatibilityLaunchPath,
            "RUNASADMIN",
            Microsoft.Win32.RegistryValueKind.String);
    }

    try
    {
        var compatibilityProfile = ShortcutResolver.Import(compatibilityLaunchPath);
        compatibilityProfile.Arguments = "/d /c exit 0";
        using var launched = LaunchService.Start(compatibilityProfile, out var launchRepair);
        Assert(launchRepair.UserRuleChanged, "launch flow repaired RUNASADMIN");
        Assert(launched != null, "compatibility test process started");
        Assert(launched!.WaitForExit(10_000), "compatibility test process exited");
        Assert(launched.ExitCode == 0, "compatibility test process exit code");
    }
    finally
    {
        using var layers = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            AppCompatibilityService.LayersRegistryPath, writable: true);
        layers?.DeleteValue(compatibilityLaunchPath, throwOnMissingValue: false);
    }

    var trackedProfile = ShortcutResolver.Import(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
    trackedProfile.Arguments = "/d /c exit 0";
    using (var tracked = LaunchService.StartTracked(trackedProfile))
    {
        Assert(tracked.Id > 0, "tracked direct child start");
        Assert(tracked.WaitForExit(10_000), "tracked direct child exit");
        Assert(tracked.ExitCode == 0, "tracked direct child exit code");
    }

    // Enumeration must be safe even on a non-interactive CI desktop, where
    // Windows may legitimately expose no user-facing top-level windows.
    var discoveredWindows = WindowDiscoveryService.Discover();
    Assert(discoveredWindows.All(window => window.ProcessId > 0), "safe window discovery");

    Console.WriteLine("Smoke tests passed.");
}
finally
{
    try { Directory.Delete(directory, recursive: true); } catch { }
}

static bool PathsEqual(string left, string right) =>
    string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Assertion failed: " + name);
}
