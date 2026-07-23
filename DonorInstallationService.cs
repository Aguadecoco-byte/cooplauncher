using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace RemotePlayLauncher;

public static class DonorInstallationService
{
    private static readonly string[] RuntimeDependencies =
    [
        "D3DCompiler_47_cor3.dll",
        "PenImc_cor3.dll",
        "PresentationNative_cor3.dll",
        "vcruntime140_cor3.dll",
        "wpfgfx_cor3.dll"
    ];

    public static string CanonicalLauncherPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "Coop Launcher",
        "CoopLauncher.exe");

    public static bool IsRunningFromConfiguredDonor(LauncherConfig config)
    {
        var current = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(current)
               && !string.IsNullOrWhiteSpace(config.DonorExePath)
               && PathsEqual(current, config.DonorExePath);
    }

    public static DonorInstallationState Install(SteamGame game, LauncherConfig config)
    {
        if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
            throw new InvalidOperationException(
                "No se pudo identificar el ejecutable principal del donante. Elige otro juego o corrige su ejecutable.");

        var source = GetTrustedSourceExecutable();
        var target = Path.GetFullPath(game.ExecutablePath);

        if (config.DonorInstallation != null && !PathsEqual(config.DonorInstallation.TargetPath, target))
            Restore(config);
        else if (!string.IsNullOrWhiteSpace(config.DonorExePath)
                 && !PathsEqual(config.DonorExePath, target)
                 && File.Exists(config.DonorExePath + ".bak"))
            RestoreLegacy(config);

        var targetInfo = FileVersionInfo.GetVersionInfo(target);
        var targetIsLauncher = string.Equals(targetInfo.ProductName, "Coop Launcher", StringComparison.OrdinalIgnoreCase);
        var legacyBackup = target + ".bak";
        var managedBackup = target + ".cooplauncher.original";
        string backup;

        if (targetIsLauncher)
        {
            backup = File.Exists(managedBackup) ? managedBackup
                   : File.Exists(legacyBackup) ? legacyBackup
                   : throw new InvalidOperationException(
                       "El juego ya contiene Coop Launcher, pero no existe un respaldo original verificable.");
        }
        else
        {
            backup = File.Exists(managedBackup) ? managedBackup : managedBackup;
            if (!File.Exists(backup))
                File.Copy(target, backup, overwrite: false);
        }

        if (string.Equals(FileVersionInfo.GetVersionInfo(backup).ProductName, "Coop Launcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El respaldo del donante también contiene Coop Launcher; se canceló para proteger el juego.");

        var state = new DonorInstallationState
        {
            AppId = game.AppId,
            GameName = game.Name,
            TargetPath = target,
            BackupPath = backup,
            OriginalSha256 = ComputeSha256(backup),
            LauncherSha256 = ComputeSha256(source),
            LauncherVersion = FileVersionInfo.GetVersionInfo(source).FileVersion ?? "unknown",
            InstalledAt = DateTimeOffset.UtcNow
        };

        var staging = target + ".cooplauncher.new";
        var createdSidecars = new List<string>();
        try
        {
            File.Copy(source, staging, overwrite: true);
            if (!string.Equals(ComputeSha256(staging), state.LauncherSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La copia temporal del launcher no superó la verificación SHA-256.");
            File.Move(staging, target, overwrite: true);

            var sourceDirectory = Path.GetDirectoryName(source)!;
            var targetDirectory = Path.GetDirectoryName(target)!;
            foreach (var dependency in RuntimeDependencies)
            {
                var dependencySource = Path.Combine(sourceDirectory, dependency);
                var dependencyTarget = Path.Combine(targetDirectory, dependency);
                if (!File.Exists(dependencySource) || File.Exists(dependencyTarget))
                    continue;

                File.Copy(dependencySource, dependencyTarget, overwrite: false);
                var hash = ComputeSha256(dependencyTarget);
                state.CreatedSidecars[dependency] = hash;
                createdSidecars.Add(dependencyTarget);
            }

            config.DonorExePath = target;
            config.DonorAppId = game.AppId;
            config.DonorInstallation = state;
            config.Save();
            AppLog.Write($"Installed launcher {state.LauncherVersion} into donor {game.Name} ({game.AppId}).");
            return state;
        }
        catch
        {
            TryDelete(staging);
            foreach (var sidecar in createdSidecars) TryDelete(sidecar);
            try { File.Copy(backup, target, overwrite: true); }
            catch (Exception rollbackError) { AppLog.Write("Donor rollback failed.", rollbackError); }
            throw;
        }
    }

    public static void Restore(LauncherConfig config)
    {
        var state = config.DonorInstallation;
        if (state == null)
        {
            RestoreLegacy(config);
            return;
        }

        if (!File.Exists(state.BackupPath))
            throw new FileNotFoundException("No se encontró el respaldo original del donante.", state.BackupPath);
        if (!string.Equals(ComputeSha256(state.BackupPath), state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El respaldo original cambió; se canceló la restauración para evitar daños.");

        if (File.Exists(state.TargetPath))
        {
            var currentHash = ComputeSha256(state.TargetPath);
            if (string.Equals(currentHash, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                ClearDonorState(config);
                return;
            }

            var product = FileVersionInfo.GetVersionInfo(state.TargetPath).ProductName;
            if (!string.Equals(currentHash, state.LauncherSha256, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(product, "Coop Launcher", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Steam u otra aplicación modificó el ejecutable del donante. No se restaurará automáticamente.");
        }

        RestoreFileAtomically(state.BackupPath, state.TargetPath);
        RemoveRecordedSidecars(state);
        AppLog.Write($"Restored donor {state.GameName} ({state.AppId}).");
        ClearDonorState(config);
    }

    public static bool IsHealthy(LauncherConfig config, out string detail)
    {
        var state = config.DonorInstallation;
        if (state == null)
        {
            detail = "No hay una instalación administrada.";
            return false;
        }
        if (!File.Exists(state.TargetPath))
        {
            detail = "Falta el ejecutable del donante.";
            return false;
        }
        if (!File.Exists(state.BackupPath))
        {
            detail = "Falta el respaldo original.";
            return false;
        }

        var product = FileVersionInfo.GetVersionInfo(state.TargetPath).ProductName;
        var healthy = string.Equals(product, "Coop Launcher", StringComparison.OrdinalIgnoreCase);
        detail = healthy ? $"Instalación verificada ({state.LauncherVersion})." : "El ejecutable ya no es Coop Launcher.";
        return healthy;
    }

    private static string GetTrustedSourceExecutable()
    {
        if (File.Exists(CanonicalLauncherPath)
            && string.Equals(FileVersionInfo.GetVersionInfo(CanonicalLauncherPath).ProductName,
                "Coop Launcher", StringComparison.OrdinalIgnoreCase))
            return CanonicalLauncherPath;

        var current = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
            return current;
        throw new FileNotFoundException("No se encontró una instalación canónica de Coop Launcher.");
    }

    private static void RestoreLegacy(LauncherConfig config)
    {
        var target = config.DonorExePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            ClearDonorState(config);
            return;
        }

        var backup = target + ".bak";
        if (!File.Exists(backup))
            throw new FileNotFoundException("No se encontró el respaldo legado del donante.", backup);
        if (File.Exists(target)
            && !string.Equals(FileVersionInfo.GetVersionInfo(target).ProductName,
                "Coop Launcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El ejecutable del donante ya no pertenece a Coop Launcher.");

        RestoreFileAtomically(backup, target);
        AppLog.Write($"Restored legacy donor at {target}.");
        ClearDonorState(config);
    }

    private static void RestoreFileAtomically(string backup, string target)
    {
        var staging = target + ".cooplauncher.restore";
        File.Copy(backup, staging, overwrite: true);
        File.Move(staging, target, overwrite: true);
    }

    private static void RemoveRecordedSidecars(DonorInstallationState state)
    {
        var directory = Path.GetDirectoryName(state.TargetPath)!;
        foreach (var sidecar in state.CreatedSidecars)
        {
            var path = Path.Combine(directory, sidecar.Key);
            if (File.Exists(path)
                && string.Equals(ComputeSha256(path), sidecar.Value, StringComparison.OrdinalIgnoreCase))
                TryDelete(path);
        }
    }

    private static void ClearDonorState(LauncherConfig config)
    {
        config.DonorExePath = null;
        config.DonorAppId = null;
        config.DonorInstallation = null;
        config.Save();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Write($"Could not remove {path}.", ex); }
    }
}
