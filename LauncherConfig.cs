using System.IO;
using System.Text.Json;

namespace RemotePlayLauncher;

public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 2;
    public string? DonorExePath { get; set; }
    public string? DonorAppId { get; set; }
    public DonorInstallationState? DonorInstallation { get; set; }
    public Dictionary<string, string> ExecutableOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<CustomLaunchProfile> CustomGames { get; set; } = [];
    public bool OverlayCompatibilityMode { get; set; } = true;
    public bool MaximizeInDonorMode { get; set; } = true;

    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoopLauncher");

    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "launcher_config.json");

    public static LauncherConfig Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigPath))
                return new LauncherConfig();

            var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath))
                         ?? new LauncherConfig();
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            AppLog.Write("The configuration could not be loaded. Preserving it for recovery.", ex);
            PreserveCorruptConfig();
            return new LauncherConfig();
        }
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(ConfigDirectory);

        var temporaryPath = ConfigPath + ".tmp";
        var backupPath = ConfigPath + ".previous";
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(temporaryPath, json);
        using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stream.Flush(flushToDisk: true);

        if (File.Exists(ConfigPath))
            File.Copy(ConfigPath, backupPath, overwrite: true);
        File.Move(temporaryPath, ConfigPath, overwrite: true);
    }

    private void Normalize()
    {
        SchemaVersion = 2;
        ExecutableOverrides = ExecutableOverrides == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(ExecutableOverrides, StringComparer.OrdinalIgnoreCase);
        CustomGames ??= [];

        foreach (var profile in CustomGames)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = Guid.NewGuid().ToString("N");
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Custom game" : profile.Name.Trim();
            profile.SourcePath ??= string.Empty;
            profile.ExecutablePath ??= string.Empty;
            profile.Arguments ??= string.Empty;
            profile.WorkingDirectory ??= string.Empty;
            profile.IconPath ??= string.Empty;
        }
    }

    private static void PreserveCorruptConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var destination = Path.Combine(
                ConfigDirectory,
                $"launcher_config.corrupt.{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, destination, overwrite: false);
        }
        catch (Exception ex)
        {
            AppLog.Write("The corrupt configuration could not be copied.", ex);
        }
    }
}
