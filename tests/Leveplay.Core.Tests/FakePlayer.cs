using Leveplay.Core.Devices;

namespace Leveplay.Core.Tests;

/// <summary>In-memory <see cref="IPlayerGateway"/> that records the commands it receives.</summary>
internal sealed class FakePlayer(params ConnectDevice[] devices) : IPlayerGateway
{
    /// <summary>What <see cref="GetPlaybackAsync"/> returns; null means no active device.</summary>
    public PlaybackState? Playback { get; init; }

    public int PlaybackCalls { get; private set; }

    /// <summary>Device ids playback was transferred to, in order.</summary>
    public List<string> Transfers { get; } = [];

    public List<bool> PlayFlags { get; } = [];

    /// <summary>Every other command, e.g. "pause" or "play spotify:playlist:x on pc-id".</summary>
    public List<string> Commands { get; } = [];

    public Task<IReadOnlyList<ConnectDevice>> GetDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConnectDevice>>(devices);

    public Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default)
    {
        PlaybackCalls++;
        return Task.FromResult(Playback);
    }

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken ct = default)
    {
        Transfers.Add(deviceId);
        PlayFlags.Add(play);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default) => Record("pause");

    public Task ResumeAsync(CancellationToken ct = default) => Record("resume");

    public Task PlayContextAsync(string contextUri, string deviceId, CancellationToken ct = default) =>
        Record($"play {contextUri} on {deviceId}");

    public Task NextAsync(CancellationToken ct = default) => Record("next");

    public Task PreviousAsync(CancellationToken ct = default) => Record("previous");

    public Task SetVolumeAsync(int percent, CancellationToken ct = default) => Record($"volume {percent}");

    public Task SeekAsync(int positionMs, CancellationToken ct = default) => Record($"seek {positionMs}");

    private Task Record(string command)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }
}
