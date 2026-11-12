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

    [Theory]
    [InlineData(40, "volume 40")]
    [InlineData(150, "volume 100")]
    [InlineData(-5, "volume 0")]
    public async Task Volume_is_clamped_to_the_valid_range(int requested, string expected)
    {
        var player = new FakePlayer(Pc);

        await new PlaybackController(player).SetVolumeAsync(requested);

        Assert.Equal([expected], player.Commands);
    }

    [Fact]
    public async Task Seek_never_goes_before_the_start()
    {
        var player = new FakePlayer(Pc);
        var controller = new PlaybackController(player);

        await controller.SeekAsync(90_000);
        await controller.SeekAsync(-10);

        Assert.Equal(["seek 90000", "seek 0"], player.Commands);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59_999, "0:59")]
    [InlineData(84_000, "1:24")]
    [InlineData(3_723_000, "1:02:03")]
    [InlineData(-500, "0:00")]
    public void Formats_positions(int milliseconds, string expected)
    {
        Assert.Equal(expected, PlaybackController.FormatTime(milliseconds));
    }

    [Fact]
    public void Picks_the_smallest_cover_that_is_large_enough()
    {
        var images = new[] { ("big", 640), ("medium", 300), ("small", 64) };

        Assert.Equal("medium", PlaybackController.PickArtUrl(images));
        Assert.Equal("small", PlaybackController.PickArtUrl(images, minWidth: 48));
    }

    [Fact]
    public void Falls_back_to_the_widest_cover_and_handles_missing_art()
    {
        Assert.Equal("b", PlaybackController.PickArtUrl([("a", 32), ("b", 64)]));
        Assert.Null(PlaybackController.PickArtUrl([]));
        Assert.Null(PlaybackController.PickArtUrl(null));
    }

    [Fact]
    public void Describes_the_absence_of_an_active_device()
    {
        Assert.Equal("no active device", PlaybackController.Describe(null));
    }
}
