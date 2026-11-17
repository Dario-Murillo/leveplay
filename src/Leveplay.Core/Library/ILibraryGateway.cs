namespace Leveplay.Core.Library;

/// <summary>A playlist in the user's library, with just what a list needs to show it.</summary>
/// <param name="Uri">Context URI, e.g. <c>spotify:playlist:…</c>, used to start playback.</param>
/// <param name="TrackCount">
/// Null when Spotify does not say: since February 2026 it only reports the contents of
/// playlists the user owns or collaborates on.
/// </param>
public sealed record PlaylistSummary(string Id, string Uri, string Name, string? Owner, int? TrackCount)
{
    /// <summary>Secondary line for a list, e.g. "Dario · 42 tracks".</summary>
    public string Details
    {
        get
        {
            var tracks = TrackCount switch
            {
                null => null,
                1 => "1 track",
                _ => $"{TrackCount} tracks",
            };
            var owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner;
            return string.Join(" · ", new[] { owner, tracks }.Where(part => part is not null));
        }
    }
}

/// <summary>A track or episode inside a playlist.</summary>
/// <param name="Artist">Artists of a track, or the show of an episode.</param>
/// <param name="IsLocal">Local files cannot be started through the Web API.</param>
public sealed record TrackSummary(
    string Uri, string Name, string? Artist, string? Album, int DurationMs, bool IsLocal);

/// <summary>
/// Raised when Spotify refuses to list a playlist's tracks. It only allows that for
/// playlists the user owns or collaborates on; others can still be played as a whole.
/// </summary>
public sealed class LibraryAccessException(string message) : Exception(message);

/// <summary>Read access to the user's Spotify library.</summary>
public interface ILibraryGateway
{
    /// <summary>All playlists the user owns or follows, in the order Spotify returns them.</summary>
    Task<IReadOnlyList<PlaylistSummary>> GetPlaylistsAsync(CancellationToken ct = default);

    /// <exception cref="LibraryAccessException">The playlist's contents are not readable.</exception>
    Task<IReadOnlyList<TrackSummary>> GetPlaylistTracksAsync(string playlistId, CancellationToken ct = default);
}
