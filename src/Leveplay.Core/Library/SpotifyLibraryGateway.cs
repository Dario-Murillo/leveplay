using SpotifyAPI.Web;

namespace Leveplay.Core.Library;

/// <summary>Adapter from <see cref="ILibraryGateway"/> to the Spotify Web API client.</summary>
public sealed class SpotifyLibraryGateway(ISpotifyClient client) : ILibraryGateway
{
    /// <summary>Largest page Spotify allows, to keep the number of requests down.</summary>
    private const int PageSize = 50;

    public async Task<IReadOnlyList<PlaylistSummary>> GetPlaylistsAsync(CancellationToken ct = default)
    {
        var firstPage = await client.Playlists
            .CurrentUsers(new PlaylistCurrentUsersRequest { Limit = PageSize }, ct)
            .ConfigureAwait(false);
        var playlists = await client.PaginateAll(firstPage, cancellationToken: ct).ConfigureAwait(false);

        return playlists
            // Spotify returns null entries for playlists that are no longer available.
            .Where(p => p is { Uri: not null, Name: not null })
            .Select(p => new PlaylistSummary(p.Uri!, p.Name!, p.Owner?.DisplayName, p.Tracks?.Total ?? 0))
            .ToList();
    }
}
