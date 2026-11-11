using Leveplay.Core.Devices;

namespace Leveplay.Core.Tests;

public sealed class PlaybackControllerTests
{
    private const string Playlist = "spotify:playlist:abc";

    private static readonly ConnectDevice Pc = new("pc-id", "Leveplay", "Computer", false, false);

    private static PlaybackState State(bool playing, string? title = "Song") =>
        new(playing, title, title is null ? null : "Artist", "Leveplay");

    [Fact]
    public async Task Pauses_when_something_is_playing()
    {
        var player = new FakePlayer(Pc) { Playback = State(playing: true) };

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", Playlist);

        Assert.Equal(PlayPauseOutcome.Paused, outcome);
        Assert.Equal(["pause"], player.Commands);
    }

    [Fact]
    public async Task Resumes_the_loaded_track_instead_of_restarting_the_default_playlist()
    {
        var player = new FakePlayer(Pc) { Playback = State(playing: false) };

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", Playlist);

        Assert.Equal(PlayPauseOutcome.Resumed, outcome);
        Assert.Equal(["resume"], player.Commands);
    }

    [Fact]
    public async Task Starts_the_default_playlist_when_no_device_is_active()
    {
        var player = new FakePlayer(Pc);

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", Playlist);

        Assert.Equal(PlayPauseOutcome.StartedDefault, outcome);
        Assert.Equal([$"play {Playlist} on pc-id"], player.Commands);
    }

    [Fact]
    public async Task Starts_the_default_playlist_when_the_active_device_has_nothing_loaded()
    {
        var player = new FakePlayer(Pc) { Playback = State(playing: false, title: null) };

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", Playlist);

        Assert.Equal(PlayPauseOutcome.StartedDefault, outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Without_a_default_playlist_asks_the_device_to_resume(string? defaultContextUri)
    {
        var player = new FakePlayer(Pc);

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", defaultContextUri);

        Assert.Equal(PlayPauseOutcome.Resumed, outcome);
        Assert.Equal(["pc-id"], player.Transfers);
        Assert.Equal([true], player.PlayFlags);
        Assert.Empty(player.Commands);
    }

    [Fact]
    public async Task Reports_no_device_when_nothing_is_active_and_the_configured_device_is_offline()
    {
        var player = new FakePlayer(new ConnectDevice("phone-id", "Phone", "Smartphone", false, false));

        var outcome = await new PlaybackController(player).TogglePlayPauseAsync("Leveplay", Playlist);

        Assert.Equal(PlayPauseOutcome.NoDevice, outcome);
        Assert.Empty(player.Commands);
        Assert.Empty(player.Transfers);
    }

    [Fact]
    public async Task Skip_commands_are_forwarded()
    {
        var player = new FakePlayer(Pc);
        var controller = new PlaybackController(player);

        await controller.NextAsync();
        await controller.PreviousAsync();

        Assert.Equal(["next", "previous"], player.Commands);
    }

    [Theory]
    [InlineData(true, "Song", "Artist", "▶ Song — Artist")]
    [InlineData(false, "Song", "Artist", "⏸ Song — Artist")]
    [InlineData(true, "Episode", null, "▶ Episode")]
    [InlineData(false, null, null, "nothing playing on Leveplay")]
    public void Describes_the_playback_state(bool playing, string? title, string? artist, string expected)
    {
        Assert.Equal(expected, PlaybackController.Describe(new PlaybackState(playing, title, artist, "Leveplay")));
    }

    [Fact]
    public void Describes_the_absence_of_an_active_device()
    {
        Assert.Equal("no active device", PlaybackController.Describe(null));
    }
}
