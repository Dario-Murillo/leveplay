using Leveplay.Core.Auth;
using Leveplay.Core.Library;
using Newtonsoft.Json;

namespace Leveplay.Core.Tests;

public sealed class LibraryTests
{
    [Theory]
    [InlineData("Dario", 42, "Dario · 42 tracks")]
    [InlineData("Dario", 1, "Dario · 1 track")]
    [InlineData(null, 0, "0 tracks")]
    [InlineData("Spotify", null, "Spotify")]
    [InlineData("  ", null, "")]
    public void Playlist_details_show_owner_and_size(string? owner, int? tracks, string expected)
    {
        Assert.Equal(expected, new PlaylistSummary("x", "spotify:playlist:x", "Mix", owner, tracks).Details);
    }

    [Fact]
    public void Reads_the_track_count_from_the_2026_items_field()
    {
        var dto = JsonConvert.DeserializeObject<PlaylistDto>("""
            {
              "id": "p1", "uri": "spotify:playlist:p1", "name": "Gym",
              "owner": { "display_name": "Dario" },
              "items": { "href": "https://api.spotify.com/v1/playlists/p1/items", "total": 86 }
            }
            """);

        Assert.Equal(new PlaylistSummary("p1", "spotify:playlist:p1", "Gym", "Dario", 86), LibraryMapper.ToSummary(dto));
    }

    [Fact]
    public void Falls_back_to_the_legacy_tracks_field()
    {
        var dto = JsonConvert.DeserializeObject<PlaylistDto>("""
            { "id": "p1", "uri": "spotify:playlist:p1", "name": "Gym", "tracks": { "total": 7 } }
            """);

        Assert.Equal(7, LibraryMapper.ToSummary(dto)!.TrackCount);
    }

    [Fact]
    public void A_playlist_without_contents_has_an_unknown_track_count()
    {
        var dto = JsonConvert.DeserializeObject<PlaylistDto>("""
            { "id": "p2", "uri": "spotify:playlist:p2", "name": "Discover Weekly", "owner": { "display_name": "Spotify" } }
            """);

        Assert.Null(LibraryMapper.ToSummary(dto)!.TrackCount);
    }

    [Fact]
    public void Unavailable_playlists_are_skipped()
    {
        Assert.Null(LibraryMapper.ToSummary((PlaylistDto?)null));
        Assert.Null(LibraryMapper.ToSummary(new PlaylistDto { Id = "p", Name = "No uri" }));
    }

    [Fact]
    public void Reads_a_track_from_the_2026_item_field()
    {
        var dto = JsonConvert.DeserializeObject<PlaylistItemDto>("""
            {
              "added_at": "2026-01-01T00:00:00Z", "is_local": false,
              "item": {
                "uri": "spotify:track:t1", "name": "Sing It Back", "duration_ms": 278000,
                "artists": [ { "name": "Moloko" }, { "name": "Boris Dlugosch" } ],
                "album": { "name": "I Am Not a Doctor" }
              }
            }
            """);

        Assert.Equal(
            new TrackSummary("spotify:track:t1", "Sing It Back", "Moloko, Boris Dlugosch", "I Am Not a Doctor", 278000, false),
            LibraryMapper.ToSummary(dto));
    }

    [Fact]
    public void Reads_a_track_from_the_legacy_track_field_and_an_episode_by_its_show()
    {
        var legacy = JsonConvert.DeserializeObject<PlaylistItemDto>("""
            { "track": { "uri": "spotify:track:t2", "name": "Old", "duration_ms": 1000, "artists": [ { "name": "A" } ] } }
            """);
        var episode = JsonConvert.DeserializeObject<PlaylistItemDto>("""
            { "item": { "uri": "spotify:episode:e1", "name": "Ep 1", "duration_ms": 60000, "show": { "name": "The Show" } } }
            """);

        Assert.Equal("A", LibraryMapper.ToSummary(legacy)!.Artist);
        Assert.Equal("The Show", LibraryMapper.ToSummary(episode)!.Artist);
    }

    [Fact]
    public void Removed_tracks_are_skipped_and_local_files_are_flagged()
    {
        var removed = JsonConvert.DeserializeObject<PlaylistItemDto>("""{ "item": null, "is_local": false }""");
        var local = JsonConvert.DeserializeObject<PlaylistItemDto>("""
            { "is_local": true, "item": { "uri": "spotify:local:x", "name": "My file", "duration_ms": 5 } }
            """);

        Assert.Null(LibraryMapper.ToSummary(removed));
        Assert.True(LibraryMapper.ToSummary(local)!.IsLocal);
    }
}

public sealed class RequiredScopesTests
{
    private const string Current =
        "user-read-playback-state user-modify-playback-state user-read-currently-playing " +
        "playlist-read-private playlist-read-collaborative";

    [Fact]
    public void A_token_with_every_scope_is_accepted_in_any_order()
    {
        Assert.True(SpotifyAuthService.HasRequiredScopes(Current));
        Assert.True(SpotifyAuthService.HasRequiredScopes(
            "playlist-read-collaborative extra-scope " + Current));
    }

    [Fact]
    public void A_token_from_before_playlists_needs_consent_again()
    {
        Assert.False(SpotifyAuthService.HasRequiredScopes(
            "user-read-playback-state user-modify-playback-state user-read-currently-playing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_scope_list_does_not_force_a_new_login(string? scopes)
    {
        Assert.True(SpotifyAuthService.HasRequiredScopes(scopes));
    }
}
