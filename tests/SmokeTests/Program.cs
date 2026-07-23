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
