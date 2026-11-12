using System.Text.Json.Serialization;

namespace Leveplay.Core.Config;

/// <summary>
/// User-editable settings. Defaults live in <c>appsettings.json</c>; machine-specific
/// values (such as the Client ID) go in <c>appsettings.local.json</c>, which is git-ignored.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Client ID of the app registered in the Spotify Developer Dashboard.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Loopback port for the OAuth callback. Must match the registered Redirect URI.</summary>
    public int RedirectPort { get; set; } = 5543;

    /// <summary>Spotify Connect device to control; also the name the audio engine announces.</summary>
    public string DeviceName { get; set; } = "Leveplay";

    /// <summary>Playlist or album URI to start when nothing is playing yet.</summary>
    public string? DefaultContextUri { get; set; }

    /// <summary>How often the player state is polled while a device is active.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Full path to <c>soloist.exe</c>, for when it is not in Leveplay's own engine
    /// folder (<c>%LOCALAPPDATA%\Leveplay\engine</c>). Environment variables are expanded.
    /// </summary>
    public string? EnginePath { get; set; }

    [JsonIgnore]
    public Uri RedirectUri => new($"http://127.0.0.1:{RedirectPort}/callback");
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
