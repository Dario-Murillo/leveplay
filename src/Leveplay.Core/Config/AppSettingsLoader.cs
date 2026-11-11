using System.Text.Json;
using System.Text.Json.Nodes;

namespace Leveplay.Core.Config;

public sealed class SettingsException(string message) : Exception(message);

public static class AppSettingsLoader
{
    public const string BaseFileName = "appsettings.json";
    public const string LocalFileName = "appsettings.local.json";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads <c>appsettings.json</c> from <paramref name="directory"/> and overlays
    /// <c>appsettings.local.json</c> on top of it. Either file may be missing.
    /// </summary>
    public static AppSettings Load(string directory)
    {
        var merged = new JsonObject();
        foreach (var fileName in new[] { BaseFileName, LocalFileName })
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions);
            }
            catch (JsonException ex)
            {
                throw new SettingsException($"{fileName} is not valid JSON: {ex.Message}");
            }

            if (node is not JsonObject obj)
            {
                throw new SettingsException($"{fileName} must contain a JSON object.");
            }

            foreach (var (key, value) in obj)
            {
                merged[key] = value?.DeepClone();
            }
        }

        AppSettings settings;
        try
        {
            settings = merged.Deserialize(AppJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            throw new SettingsException($"Settings have an invalid value: {ex.Message}");
        }

        Validate(settings);
        return settings;
    }

    private static void Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new SettingsException(
                $"ClientId is missing. Create {LocalFileName} next to the executable with " +
                "{ \"ClientId\": \"<your Spotify app client id>\" }.");
        }

        if (settings.RedirectPort is < 1024 or > 65535)
        {
            throw new SettingsException("RedirectPort must be between 1024 and 65535.");
        }

        if (settings.PollIntervalSeconds < 1)
        {
            throw new SettingsException("PollIntervalSeconds must be at least 1.");
        }
    }
}
