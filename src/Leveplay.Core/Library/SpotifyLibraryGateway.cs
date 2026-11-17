using System.Globalization;
using System.Net;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace Leveplay.Core.Library;

/// <summary>
/// Reads the library straight from the Web API through the authenticated connector,
/// because the typed client predates the 2026 playlist changes (see <c>LibraryDtos</c>).
/// </summary>
public sealed class SpotifyLibraryGateway(IAPIConnector api, Action<string>? log = null)
{
    /// <summary>Largest page Spotify allows, to keep the number of requests down.</summary>
    private const int PageSize = 50;

    /// <summary>Upper bound on what one list loads, so a huge playlist cannot stall the UI.</summary>
    public const int MaxItems = 2000;

    /// <summary>All playlists the user owns or follows, in the order Spotify returns them.</summary>
    public async Task<IReadOnlyList<PlaylistSummary>> GetPlaylistsAsync(CancellationToken ct = default)
    {
        var playlists = await GetAllAsync<PlaylistDto>("me/playlists", null, ct).ConfigureAwait(false);
        return playlists.Select(LibraryMapper.ToSummary).OfType<PlaylistSummary>().ToList();
    }

    /// <exception cref="LibraryAccessException">The playlist's contents are not readable.</exception>
    public async Task<IReadOnlyList<TrackSummary>> GetPlaylistTracksAsync(
        string playlistId, CancellationToken ct = default)
    {
        try
        {
            var items = await GetAllAsync<PlaylistItemDto>(
                $"playlists/{Uri.EscapeDataString(playlistId)}/items",
                new Dictionary<string, string> { ["additional_types"] = "track,episode" },
                ct).ConfigureAwait(false);
            return items.Select(LibraryMapper.ToSummary).OfType<TrackSummary>().ToList();
        }
        catch (APIException ex) when (ex.Response?.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new LibraryAccessException(
                "Spotify only lets apps list the tracks of playlists you own or collaborate on.");
        }
    }

    /// <summary>Most results Spotify returns per search request since February 2026.</summary>
    public const int SearchLimit = 10;

    /// <summary>Tracks matching <paramref name="query"/>, best match first.</summary>
    public async Task<IReadOnlyList<TrackSummary>> SearchTracksAsync(string query, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["q"] = query,
            ["type"] = "track",
            ["limit"] = SearchLimit.ToString(CultureInfo.InvariantCulture),
        };
        var result = await api.Get<SearchDto>(new Uri("search", UriKind.Relative), parameters, ct).ConfigureAwait(false);
        return (result.Tracks?.Items ?? []).Select(t => LibraryMapper.ToSummary(t)).OfType<TrackSummary>().ToList();
    }

    private async Task<List<T?>> GetAllAsync<T>(
        string path, Dictionary<string, string>? extraParameters, CancellationToken ct)
    {
        var all = new List<T?>();
        for (var offset = 0; offset < MaxItems; offset += PageSize)
        {
            var parameters = new Dictionary<string, string>(extraParameters ?? [])
            {
                ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
                ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
            };
            var page = await api
                .Get<PageDto<T>>(new Uri(path, UriKind.Relative), parameters, ct)
                .ConfigureAwait(false);

            all.AddRange(page.Items ?? []);
            if (page.Next is null || page.Items is not { Count: > 0 })
            {
                return all;
            }
        }

        log?.Invoke($"library: {path} has more than {MaxItems} entries; showing the first {MaxItems}");
        return all;
    }
}
