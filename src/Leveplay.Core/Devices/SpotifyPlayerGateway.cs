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

    public async Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default)
    {
        // The API answers 204 with no body when no device is active.
        var playback = await client.Player
            .GetCurrentPlayback(new PlayerCurrentPlaybackRequest(), ct)
            .ConfigureAwait(false);
        if (playback?.Device is null)
        {
            return null;
        }

        var (title, artist) = playback.Item switch
        {
            FullTrack track => (track.Name, string.Join(", ", track.Artists.Select(a => a.Name))),
            FullEpisode episode => (episode.Name, episode.Show?.Name),
            _ => ((string?)null, (string?)null),
        };
        return new PlaybackState(playback.IsPlaying, title, artist, playback.Device.Name);
    }

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default) =>
        client.Player.TransferPlayback(new PlayerTransferPlaybackRequest([deviceId]) { Play = play }, ct);

    public Task PauseAsync(CancellationToken ct = default) => client.Player.PausePlayback(ct);

    public Task ResumeAsync(CancellationToken ct = default) => client.Player.ResumePlayback(ct);

    public Task PlayContextAsync(string contextUri, string deviceId, CancellationToken ct = default) =>
        client.Player.ResumePlayback(
            new PlayerResumePlaybackRequest { ContextUri = contextUri, DeviceId = deviceId }, ct);

    public Task NextAsync(CancellationToken ct = default) => client.Player.SkipNext(ct);

    public Task PreviousAsync(CancellationToken ct = default) => client.Player.SkipPrevious(ct);
}
