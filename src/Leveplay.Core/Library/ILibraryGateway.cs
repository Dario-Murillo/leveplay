namespace Leveplay.Core.Library;

/// <summary>A playlist in the user's library, with just what a list needs to show it.</summary>
/// <param name="Uri">Context URI, e.g. <c>spotify:playlist:…</c>, used to start playback.</param>
public sealed record PlaylistSummary(string Uri, string Name, string? Owner, int TrackCount)
{
    /// <summary>Secondary line for a list, e.g. "Dario · 42 tracks".</summary>
    public string Details
    {
        get
        {
            var tracks = TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";
            return string.IsNullOrWhiteSpace(Owner) ? tracks : $"{Owner} · {tracks}";
        }
    }
}

/// <summary>Read access to the user's Spotify library.</summary>
public interface ILibraryGateway
{
    /// <summary>All playlists the user owns or follows, in the order Spotify returns them.</summary>
    Task<IReadOnlyList<PlaylistSummary>> GetPlaylistsAsync(CancellationToken ct = default);
}
