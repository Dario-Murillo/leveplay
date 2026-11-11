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

    public Task TransferPlaybackAsync(string deviceId, CancellationToken ct = default) =>
        client.Player.TransferPlayback(new PlayerTransferPlaybackRequest([deviceId]) { Play = false }, ct);
}
