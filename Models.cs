namespace RemotePlayLauncher;

public enum LaunchSourceKind
{
    Steam,
    Shortcut,
    Executable,
    Url
}

public sealed class CustomLaunchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom game";
    public LaunchSourceKind SourceKind { get; set; } = LaunchSourceKind.Executable;
    public string SourcePath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public int IconIndex { get; set; }
    public bool RunAsAdministrator { get; set; }
}

public sealed class DonorInstallationState
{
    public string AppId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string OriginalSha256 { get; set; } = string.Empty;
    public string LauncherSha256 { get; set; } = string.Empty;
    public string LauncherVersion { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> CreatedSidecars { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LibraryEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DisplayPath { get; init; }
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public bool RunAsAdministrator { get; init; }
    public SteamGame? SteamGame { get; init; }
    public CustomLaunchProfile? CustomProfile { get; init; }

    public bool IsSteam => SteamGame != null;
}

public sealed class RunningWindowInfo
{
    public nint Handle { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName)
        ? Title
        : $"{Title}  —  {ProcessName}";
}
