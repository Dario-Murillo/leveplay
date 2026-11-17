using Newtonsoft.Json;

namespace Leveplay.Core.Library;

// Wire format of the playlist endpoints as of the February 2026 Web API changes, which
// renamed "tracks" to "items" and "track" to "item". SpotifyAPI.Web 7.4.2 still expects
// the old names, so these endpoints are read with our own types. The old names are kept
// as a fallback in case a response still carries them.

internal sealed class PageDto<T>
{
    [JsonProperty("items")]
    public List<T?>? Items { get; set; }

    [JsonProperty("next")]
    public string? Next { get; set; }
}

internal sealed class PlaylistDto
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("uri")]
    public string? Uri { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("owner")]
    public OwnerDto? Owner { get; set; }

    [JsonProperty("items")]
    public CountDto? Items { get; set; }

    [JsonProperty("tracks")]
    public CountDto? Tracks { get; set; }
}

internal sealed class OwnerDto
{
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }
}

internal sealed class CountDto
{
    [JsonProperty("total")]
    public int? Total { get; set; }
}

internal sealed class PlaylistItemDto
{
    [JsonProperty("item")]
    public TrackDto? Item { get; set; }

    [JsonProperty("track")]
    public TrackDto? Track { get; set; }

    [JsonProperty("is_local")]
    public bool IsLocal { get; set; }
}

internal sealed class TrackDto
{
    [JsonProperty("uri")]
    public string? Uri { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("duration_ms")]
    public int DurationMs { get; set; }

    [JsonProperty("artists")]
    public List<NamedDto?>? Artists { get; set; }

    [JsonProperty("album")]
    public NamedDto? Album { get; set; }

    /// <summary>Present on podcast episodes instead of artists and album.</summary>
    [JsonProperty("show")]
    public NamedDto? Show { get; set; }
}

internal sealed class NamedDto
{
    [JsonProperty("name")]
    public string? Name { get; set; }
}

internal static class LibraryMapper
{
    /// <summary>Returns null for entries Spotify sends for playlists that no longer exist.</summary>
    public static PlaylistSummary? ToSummary(PlaylistDto? dto) =>
        dto is { Id: not null, Uri: not null, Name: not null }
            ? new PlaylistSummary(dto.Id, dto.Uri, dto.Name, dto.Owner?.DisplayName, dto.Items?.Total ?? dto.Tracks?.Total)
            : null;

    /// <summary>Returns null for removed tracks, which arrive as an entry without content.</summary>
    public static TrackSummary? ToSummary(PlaylistItemDto? dto)
    {
        var track = dto?.Item ?? dto?.Track;
        if (track is not { Uri: not null, Name: not null })
        {
            return null;
        }

        var artists = track.Artists?.Select(a => a?.Name).Where(name => !string.IsNullOrEmpty(name)).ToList();
        var artist = artists is { Count: > 0 } ? string.Join(", ", artists) : track.Show?.Name;
        return new TrackSummary(track.Uri, track.Name, artist, track.Album?.Name, track.DurationMs, dto!.IsLocal);
    }
}
