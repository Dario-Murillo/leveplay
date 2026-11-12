namespace Leveplay.Core.Devices;

/// <summary>A Spotify Connect device as reported by the Web API.</summary>
/// <param name="Id">Device id. Spotify may omit it for devices that cannot be controlled.</param>
/// <param name="IsRestricted">True when the device does not accept Web API commands.</param>
public sealed record ConnectDevice(string? Id, string Name, string Type, bool IsActive, bool IsRestricted);

/// <summary>What the active device is doing right now.</summary>
/// <param name="Title">Track or episode name; null when nothing is loaded.</param>
/// <param name="Artist">Artists of a track, or the show of an episode.</param>
/// <param name="VolumePercent">0-100, or null when the device does not report a volume.</param>
/// <param name="ArtUrl">Cover image sized for a small player, when there is one.</param>
public sealed record PlaybackState(
    bool IsPlaying,
    string? Title,
    string? Artist,
    string? DeviceName,
    int ProgressMs = 0,
    int DurationMs = 0,
    int? VolumePercent = null,
    string? ArtUrl = null);

/// <summary>
/// The slice of the Spotify player API that Leveplay uses. Kept small so the logic on
/// top of it can be tested without network access.
/// </summary>
public interface IPlayerGateway
{
    Task<IReadOnlyList<ConnectDevice>> GetDevicesAsync(CancellationToken ct = default);

    /// <summary>Returns null when no device is active.</summary>
    Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default);

    /// <summary>Makes the device the active one.</summary>
    /// <param name="play">True to keep playing on the new device, false to leave it paused.</param>
    Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default);

    Task PauseAsync(CancellationToken ct = default);

    /// <summary>Resumes whatever is loaded on the active device.</summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Starts a playlist, album or artist on the given device.</summary>
    Task PlayContextAsync(string contextUri, string deviceId, CancellationToken ct = default);

    Task NextAsync(CancellationToken ct = default);

    Task PreviousAsync(CancellationToken ct = default);

    /// <param name="percent">0-100.</param>
    Task SetVolumeAsync(int percent, CancellationToken ct = default);

    Task SeekAsync(int positionMs, CancellationToken ct = default);
}
