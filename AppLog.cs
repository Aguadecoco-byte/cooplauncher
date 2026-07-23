using System.IO;

namespace RemotePlayLauncher;

public static class AppLog
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoopLauncher",
        "logs",
        "launcher.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded();

                var text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
                if (exception != null)
                    text += Environment.NewLine + exception;
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take down the launcher.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < 2 * 1024 * 1024)
            return;

        var previous = LogPath + ".old";
        File.Move(LogPath, previous, overwrite: true);
    }
}
