using Leveplay.Core.Auth;
using Leveplay.Core.Library;

namespace Leveplay.Core.Tests;

public sealed class LibraryTests
{
    [Theory]
    [InlineData("Dario", 42, "Dario · 42 tracks")]
    [InlineData("Dario", 1, "Dario · 1 track")]
    [InlineData(null, 0, "0 tracks")]
    [InlineData("  ", 7, "7 tracks")]
    public void Playlist_details_show_owner_and_size(string? owner, int tracks, string expected)
    {
        Assert.Equal(expected, new PlaylistSummary("spotify:playlist:x", "Mix", owner, tracks).Details);
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
