namespace Leveplay.Core.Engine;

/// <summary>
/// Finds the Soloist executable. Soloist may not be redistributed, so it is never
/// shipped with Leveplay: the user downloads it once into a folder Leveplay owns.
/// </summary>
public static class EngineLocator
{
    public const string ExecutableName = "soloist.exe";

    /// <summary><c>%LOCALAPPDATA%\Leveplay\engine</c>, where Leveplay looks by default.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leveplay", "engine");

    /// <summary>
    /// Returns the executable to run, or null when the engine is not installed.
    /// An explicitly configured path wins over the default folder.
    /// </summary>
    public static string? Resolve(string? configuredPath, string? defaultDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var candidate = Path.Combine(defaultDirectory ?? DefaultDirectory, ExecutableName);
        return File.Exists(candidate) ? candidate : null;
    }
}
