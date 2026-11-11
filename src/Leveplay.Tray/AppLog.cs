namespace Leveplay.Tray;

/// <summary>
/// Minimal diagnostic log at <c>%APPDATA%\Leveplay\leveplay.log</c>. A tray app has no
/// console, so this is the only place to see what happened. Never log tokens here.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leveplay", "leveplay.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
