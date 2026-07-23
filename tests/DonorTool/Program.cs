using RemotePlayLauncher;

if (args.Length != 2)
    throw new ArgumentException("Usage: DonorTool repair <SteamAppId> | remove-profile <ExecutablePath>");

var config = LauncherConfig.Load();
if (args[0] == "repair")
{
    var game = SteamDiscovery.Discover().FirstOrDefault(item => item.AppId == args[1])
               ?? throw new InvalidOperationException("Steam game was not found: " + args[1]);
    var state = DonorInstallationService.Install(game, config);
    Console.WriteLine($"Installed {state.LauncherVersion} into {state.TargetPath}");
    Console.WriteLine($"Original backup: {state.BackupPath}");
    return;
}

if (args[0] == "remove-profile")
{
    var fullPath = Path.GetFullPath(args[1]);
    var removed = config.CustomGames.RemoveAll(profile =>
    {
        try
        {
            return string.Equals(Path.GetFullPath(profile.ExecutablePath), fullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    });
    config.Save();
    Console.WriteLine($"Removed {removed} matching profile(s).");
    return;
}

throw new ArgumentException("Unknown command: " + args[0]);
