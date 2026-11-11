using Leveplay.Core.Devices;

namespace Leveplay.Core.Tests;

public sealed class DeviceConnectorTests
{
    private sealed class FakePlayer(params ConnectDevice[] devices) : IPlayerGateway
    {
        public List<string> Transfers { get; } = [];
        public List<bool> PlayFlags { get; } = [];
        public bool IsPlaying { get; init; }
        public int IsPlayingCalls { get; private set; }

        public Task<IReadOnlyList<ConnectDevice>> GetDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectDevice>>(devices);

        public Task<bool> IsPlayingAsync(CancellationToken ct = default)
        {
            IsPlayingCalls++;
            return Task.FromResult(IsPlaying);
        }

        public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default)
        {
            Transfers.Add(deviceId);
            PlayFlags.Add(play);
            return Task.CompletedTask;
        }
    }

    private static ConnectDevice Device(
        string name, string? id = "id-1", bool active = false, bool restricted = false) =>
        new(id, name, "Computer", active, restricted);

    [Fact]
    public async Task Transfers_playback_to_the_named_device()
    {
        var player = new FakePlayer(Device("Phone", "phone-id", active: true), Device("Leveplay", "pc-id"));

        var result = await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal(ConnectStatus.Transferred, result.Status);
        Assert.True(result.IsConnected);
        Assert.Equal(["pc-id"], player.Transfers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transfer_keeps_the_current_play_state(bool wasPlaying)
    {
        var player = new FakePlayer(Device("Phone", "phone-id", active: true), Device("Leveplay", "pc-id"))
        {
            IsPlaying = wasPlaying,
        };

        await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal([wasPlaying], player.PlayFlags);
    }

    [Fact]
    public async Task Transfer_does_not_start_playback_when_no_device_was_active()
    {
        var player = new FakePlayer(Device("Leveplay", "pc-id")) { IsPlaying = true };

        await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal([false], player.PlayFlags);
        Assert.Equal(0, player.IsPlayingCalls);
    }

    [Fact]
    public async Task Name_match_ignores_case_and_whitespace()
    {
        var player = new FakePlayer(Device("Dario's iPhone", "phone-id"));

        var result = await new DeviceConnector(player).ConnectAsync("  dario's iphone ");

        Assert.Equal(ConnectStatus.Transferred, result.Status);
        Assert.Equal(["phone-id"], player.Transfers);
    }

    [Fact]
    public async Task Does_nothing_when_the_device_is_already_active()
    {
        var player = new FakePlayer(Device("Leveplay", "pc-id", active: true));

        var result = await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal(ConnectStatus.AlreadyActive, result.Status);
        Assert.True(result.IsConnected);
        Assert.Empty(player.Transfers);
    }

    [Fact]
    public async Task Reports_available_devices_when_the_name_is_not_found()
    {
        var player = new FakePlayer(Device("Phone"), Device("TV"));

        var result = await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal(ConnectStatus.NotFound, result.Status);
        Assert.False(result.IsConnected);
        Assert.Null(result.Device);
        Assert.Equal(["Phone", "TV"], result.AvailableDeviceNames);
        Assert.Empty(player.Transfers);
    }

    [Fact]
    public async Task Reports_not_found_when_no_device_is_online()
    {
        var result = await new DeviceConnector(new FakePlayer()).ConnectAsync("Leveplay");

        Assert.Equal(ConnectStatus.NotFound, result.Status);
        Assert.Empty(result.AvailableDeviceNames);
    }

    [Theory]
    [InlineData("id-1", true)]
    [InlineData(null, false)]
    public async Task Does_not_transfer_to_a_device_that_cannot_be_controlled(string? id, bool restricted)
    {
        var player = new FakePlayer(Device("Leveplay", id, restricted: restricted));

        var result = await new DeviceConnector(player).ConnectAsync("Leveplay");

        Assert.Equal(ConnectStatus.Restricted, result.Status);
        Assert.False(result.IsConnected);
        Assert.Empty(player.Transfers);
    }

    [Fact]
    public void A_controllable_device_wins_over_a_restricted_one_with_the_same_name()
    {
        var devices = new[] { Device("Leveplay", "a", restricted: true), Device("Leveplay", "b") };

        Assert.Equal("b", DeviceConnector.FindByName(devices, "Leveplay")!.Id);
    }
}
