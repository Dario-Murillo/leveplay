namespace Leveplay.Core.Devices;

/// <summary>A Spotify Connect device as reported by the Web API.</summary>
/// <param name="Id">Device id. Spotify may omit it for devices that cannot be controlled.</param>
/// <param name="IsRestricted">True when the device does not accept Web API commands.</param>
public sealed record ConnectDevice(string? Id, string Name, string Type, bool IsActive, bool IsRestricted);

/// <summary>
/// The slice of the Spotify player API that Leveplay uses. Kept small so the logic on
/// top of it can be tested without network access.
/// </summary>
public interface IPlayerGateway
{
    Task<IReadOnlyList<ConnectDevice>> GetDevicesAsync(CancellationToken ct = default);

    /// <summary>True when something is playing right now on any device.</summary>
    Task<bool> IsPlayingAsync(CancellationToken ct = default);

    /// <summary>Makes the device the active one.</summary>
    /// <param name="play">True to keep playing on the new device, false to leave it paused.</param>
    Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default);
}
