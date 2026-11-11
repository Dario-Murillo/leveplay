using SpotifyAPI.Web;

namespace Leveplay.Core.Devices;

/// <summary>Adapter from <see cref="IPlayerGateway"/> to the Spotify Web API client.</summary>
public sealed class SpotifyPlayerGateway(ISpotifyClient client) : IPlayerGateway
{
    public async Task<IReadOnlyList<ConnectDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var response = await client.Player.GetAvailableDevices(ct).ConfigureAwait(false);
        return response.Devices
            .Select(d => new ConnectDevice(d.Id, d.Name, d.Type, d.IsActive, d.IsRestricted))
            .ToList();
    }

    public async Task<bool> IsPlayingAsync(CancellationToken ct = default)
    {
        // The API answers 204 with no body when there is no active playback.
        var playback = await client.Player.GetCurrentPlayback(ct).ConfigureAwait(false);
        return playback?.IsPlaying ?? false;
    }

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default) =>
        client.Player.TransferPlayback(new PlayerTransferPlaybackRequest([deviceId]) { Play = play }, ct);
}
