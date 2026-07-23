using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RemotePlayLauncher;

public enum SteamConflictKind
{
    MillenniumBootstrapper,
    ValeSteamTools
}

public sealed record SteamConflict(
    string FilePath,
    string FileName,
    string DisplayName,
    string ProductName,
    string CompanyName);

public sealed record SteamConflictInspection(
    string SteamRoot,
    IReadOnlyList<SteamConflict> ActiveConflicts,
    IReadOnlyList<string> DisabledBackups,
    long RunningAppId);

public sealed record SteamConflictRepairResult(
    bool Success,
    bool Changed,
    bool SteamRestarted,
    IReadOnlyList<string> DisabledNames,
    string Message);

public static class SteamConflictRepairService
{
    private static readonly string[] KnownFileNames = ["wsock32.dll", "xinput1_4.dll"];

    public static SteamConflictInspection Inspect()
    {
        var steamRoot = FindSteamRoot();
        var conflicts = new List<SteamConflict>();
        var backups = new List<string>();

        foreach (var fileName in KnownFileNames)
        {
            var filePath = Path.Combine(steamRoot, fileName);
            if (File.Exists(filePath))
            {
                var version = FileVersionInfo.GetVersionInfo(filePath);
                var conflictKind = ClassifyKnownConflict(
                    fileName,
                    version.ProductName,
                    version.CompanyName);
                if (conflictKind != null)
                {
                    conflicts.Add(new SteamConflict(
                        filePath,
                        fileName,
                        GetDisplayName(conflictKind.Value),
                        version.ProductName ?? string.Empty,
                        version.CompanyName ?? string.Empty));
                }
            }

            foreach (var candidate in Directory.EnumerateFiles(steamRoot, fileName + ".*"))
            {
                if (!IsDisabledBackupName(Path.GetFileName(candidate), fileName)) continue;
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(candidate);
                    if (ClassifyKnownConflict(fileName, version.ProductName, version.CompanyName) != null)
                        backups.Add(candidate);
                }
                catch { }
            }
        }

        return new SteamConflictInspection(
            steamRoot,
            conflicts,
            backups.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            GetRunningAppId());
    }

    public static async Task<SteamConflictRepairResult> RepairAsync(CancellationToken cancellationToken)
    {
        var inspection = Inspect();
        if (inspection.ActiveConflicts.Count == 0)
        {
            return new SteamConflictRepairResult(
                true,
                false,
                false,
                [],
                inspection.DisabledBackups.Count > 0
                    ? "Steam ya estaba reparado. Los archivos conflictivos continúan respaldados y desactivados."
                    : "No se detectaron modificaciones conocidas que requieran reparación.");
        }

        if (inspection.RunningAppId != 0 || IsProcessRunning("streaming_client"))
        {
            return new SteamConflictRepairResult(
                false,
                false,
                false,
                [],
                "Steam tiene un juego o una sesión de Remote Play activa. Ciérrala y vuelve a intentarlo.");
        }

        var steamWasRunning = IsProcessRunning("steam");
        if (steamWasRunning)
        {
            if (!RequestSteamShutdown(inspection.SteamRoot, out var shutdownError))
            {
                return new SteamConflictRepairResult(
                    false,
                    false,
                    false,
                    [],
                    "No se pudo solicitar el cierre normal de Steam. " + shutdownError);
            }

            bool steamExited;
            try
            {
                steamExited = await WaitForProcessExitAsync(
                    "steam",
                    TimeSpan.FromSeconds(40),
                    cancellationToken);
            }
            catch
            {
                TryStartSteam(inspection.SteamRoot, out _);
                throw;
            }

            if (!steamExited)
            {
                return new SteamConflictRepairResult(
                    false,
                    false,
                    false,
                    [],
                    "Steam no se cerró dentro del tiempo de seguridad. No se modificó ningún archivo.");
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            if (steamWasRunning) TryStartSteam(inspection.SteamRoot, out _);
            throw;
        }

        if (IsProcessRunning("streaming_client"))
        {
            if (steamWasRunning) TryStartSteam(inspection.SteamRoot, out _);
            return new SteamConflictRepairResult(
                false,
                false,
                false,
                [],
                "El cliente de Remote Play sigue activo. No se modificó ningún archivo.");
        }

        SteamConflictInspection currentInspection;
        try
        {
            currentInspection = Inspect();
        }
        catch
        {
            if (steamWasRunning) TryStartSteam(inspection.SteamRoot, out _);
            throw;
        }
        if (currentInspection.RunningAppId != 0)
        {
            if (steamWasRunning) TryStartSteam(currentInspection.SteamRoot, out _);
            return new SteamConflictRepairResult(
                false,
                false,
                false,
                [],
                "Steam todavía informa un juego activo. No se modificó ningún archivo.");
        }

        if (currentInspection.ActiveConflicts.Count == 0)
        {
            var restartedWithoutChanges = false;
            if (steamWasRunning && TryStartSteam(currentInspection.SteamRoot, out _))
            {
                restartedWithoutChanges = await WaitForProcessStartAsync(
                    "steam",
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
                if (restartedWithoutChanges)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    restartedWithoutChanges = IsProcessRunning("steam");
                }
            }

            return new SteamConflictRepairResult(
                true,
                false,
                restartedWithoutChanges,
                [],
                "Las modificaciones ya no estaban activas al cerrar Steam. No se modificó ningún archivo." +
                (steamWasRunning && !restartedWithoutChanges
                    ? " Abre Steam manualmente para continuar."
                    : string.Empty));
        }

        var movedFiles = new List<(string Source, string Backup, string DisplayName)>();
        try
        {
            foreach (var conflict in currentInspection.ActiveConflicts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backupPath = GetAvailableBackupPath(conflict.FilePath);
                File.Move(conflict.FilePath, backupPath);
                movedFiles.Add((conflict.FilePath, backupPath, conflict.DisplayName));
                AppLog.Write(
                    $"Disabled a confirmed Steam Remote Play conflict. " +
                    $"Name={conflict.DisplayName}; Source={conflict.FilePath}; Backup={backupPath}");
            }
        }
        catch (Exception repairError)
        {
            var rollbackErrors = RollBackMoves(movedFiles);
            var steamRecovered = !steamWasRunning
                                 || TryStartSteam(currentInspection.SteamRoot, out _);
            var suffix = rollbackErrors.Count == 0
                ? " Los cambios parciales se revirtieron."
                : " Algunas copias no pudieron volver a su ubicación: " + string.Join(" | ", rollbackErrors);
            if (!steamRecovered)
                suffix += " Steam tampoco pudo reiniciarse automáticamente.";
            throw new IOException("No se pudo respaldar una modificación de Steam." + suffix, repairError);
        }

        var restarted = false;
        string? restartWarning = null;
        if (steamWasRunning)
        {
            if (TryStartSteam(currentInspection.SteamRoot, out var restartError))
            {
                restarted = true;
            }
            else
            {
                restartWarning = " La reparación terminó, pero Steam no pudo reiniciarse automáticamente: " + restartError;
                AppLog.Write("Steam was repaired but could not be restarted. " + restartError);
            }
        }

        var disabledNames = movedFiles
            .Select(item => item.DisplayName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (restarted)
        {
            var steamStarted = await WaitForProcessStartAsync(
                "steam",
                TimeSpan.FromSeconds(15),
                cancellationToken);
            if (steamStarted)
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            if (!steamStarted || !IsProcessRunning("steam"))
            {
                restarted = false;
                restartWarning =
                    " La reparación terminó, pero Steam no permaneció abierto. Inícialo manualmente para continuar.";
                AppLog.Write("Steam was repaired but did not remain running after restart.");
            }
            else
            {
                var verification = Inspect();
                if (verification.ActiveConflicts.Count > 0)
                {
                    var recreated = string.Join(
                        ", ",
                        verification.ActiveConflicts
                            .Select(conflict => conflict.DisplayName)
                            .Distinct(StringComparer.CurrentCultureIgnoreCase));
                    return new SteamConflictRepairResult(
                        false,
                        movedFiles.Count > 0,
                        true,
                        disabledNames,
                        "Los archivos se respaldaron, pero una modificación volvió a activarse al iniciar Steam: " +
                        recreated + ". Cierra su actualizador o desinstálala y pulsa «Reparar Steam» otra vez.");
                }
            }
        }

        var restartMessage = steamWasRunning
            ? (restarted
                ? " Steam se está iniciando de nuevo."
                : restartWarning)
            : " Abre Steam y acepta una invitación nueva.";
        return new SteamConflictRepairResult(
            true,
            movedFiles.Count > 0,
            restarted,
            disabledNames,
            "Se respaldaron y desactivaron: " + string.Join(", ", disabledNames) + "." + restartMessage);
    }

    public static SteamConflictKind? ClassifyKnownConflict(
        string fileName,
        string? productName,
        string? companyName)
    {
        if (string.Equals(fileName, "wsock32.dll", StringComparison.OrdinalIgnoreCase)
            && string.Equals(productName?.Trim(), "Millennium Bootstrapper", StringComparison.OrdinalIgnoreCase)
            && Contains(companyName, "Steam Homebrew"))
            return SteamConflictKind.MillenniumBootstrapper;

        if (string.Equals(fileName, "xinput1_4.dll", StringComparison.OrdinalIgnoreCase)
            && string.Equals(productName?.Trim(), "Vale", StringComparison.OrdinalIgnoreCase)
            && Contains(companyName, "Vale Corporation"))
            return SteamConflictKind.ValeSteamTools;

        return null;
    }

    public static bool IsRecognizedDisabledBackupName(string candidateName)
    {
        foreach (var originalFileName in KnownFileNames)
        {
            if (IsDisabledBackupName(candidateName, originalFileName))
                return true;
        }

        return false;
    }

    public static bool IsDisabledBackupName(string candidateName, string originalFileName)
    {
        if (!candidateName.StartsWith(originalFileName + ".", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = candidateName[(originalFileName.Length + 1)..];
        return suffix.StartsWith("cooplauncher-disabled", StringComparison.OrdinalIgnoreCase)
               || suffix.StartsWith("disabled-for-remoteplay-test-", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSteamRoot()
    {
        string? configuredPath = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            configuredPath = key?.GetValue("SteamPath") as string;
        }
        catch { }

        var candidates = new[]
        {
            configuredPath,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var fullPath = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(candidate)
                        .Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(Path.Combine(fullPath, "steam.exe")))
                    return fullPath;
            }
            catch { }
        }

        throw new DirectoryNotFoundException(
            "No se encontró una instalación válida de Steam para este usuario.");
    }

    private static long GetRunningAppId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            var value = key?.GetValue("RunningAppID");
            return value == null ? 0 : Convert.ToInt64(value);
        }
        catch
        {
            // Fail closed: if the active-game state cannot be read, the repair
            // must not assume Steam is idle and risk interrupting a game.
            return -1;
        }
    }

    private static bool RequestSteamShutdown(string steamRoot, out string error)
    {
        try
        {
            var shutdown = Process.Start(new ProcessStartInfo(Path.Combine(steamRoot, "steam.exe"))
            {
                UseShellExecute = true,
                Arguments = "-shutdown"
            });
            shutdown?.Dispose();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryStartSteam(string steamRoot, out string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(steamRoot, "steam.exe"))
            {
                UseShellExecute = true
            })?.Dispose();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(
        string processName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsProcessRunning(processName)) return true;
            await Task.Delay(500, cancellationToken);
        }

        return !IsProcessRunning(processName);
    }

    private static async Task<bool> WaitForProcessStartAsync(
        string processName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsProcessRunning(processName)) return true;
            await Task.Delay(250, cancellationToken);
        }

        return IsProcessRunning(processName);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Length > 0; }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static string GetAvailableBackupPath(string sourcePath)
    {
        var preferred = sourcePath + ".cooplauncher-disabled";
        if (!File.Exists(preferred)) return preferred;

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = preferred + "." + timestamp;
        var sequence = 1;
        while (File.Exists(candidate))
        {
            candidate = preferred + "." + timestamp + "." + sequence;
            sequence++;
        }

        return candidate;
    }

    private static List<string> RollBackMoves(
        IReadOnlyList<(string Source, string Backup, string DisplayName)> movedFiles)
    {
        var errors = new List<string>();
        for (var index = movedFiles.Count - 1; index >= 0; index--)
        {
            var move = movedFiles[index];
            try
            {
                if (!File.Exists(move.Source) && File.Exists(move.Backup))
                    File.Move(move.Backup, move.Source);
            }
            catch (Exception ex)
            {
                errors.Add(move.DisplayName + ": " + ex.Message);
            }
        }

        return errors;
    }

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string GetDisplayName(SteamConflictKind kind) => kind switch
    {
        SteamConflictKind.MillenniumBootstrapper => "Millennium",
        SteamConflictKind.ValeSteamTools => "SteamTools / Vale",
        _ => kind.ToString()
    };
}
