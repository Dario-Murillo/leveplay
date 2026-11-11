namespace Leveplay.Core.Devices;

public enum PlayPauseOutcome
{
    Paused,

    /// <summary>Resumed what was already loaded.</summary>
    Resumed,

    /// <summary>Nothing was loaded, so the default playlist was started.</summary>
    StartedDefault,

    /// <summary>Nothing is active and the configured device is not online.</summary>
    NoDevice,
}

/// <summary>Play/pause and skip commands, including what to do when nothing is playing yet.</summary>
public sealed class PlaybackController(IPlayerGateway player, Action<string>? log = null)
{
    /// <summary>
    /// Toggles playback. A plain "resume" fails when nothing has been played yet, so in
    /// that case this starts <paramref name="defaultContextUri"/> on the configured
    /// device, or, without a default, asks that device to pick up where it left off.
    /// </summary>
    public async Task<PlayPauseOutcome> TogglePlayPauseAsync(
        string deviceName, string? defaultContextUri, CancellationToken ct = default)
    {
        var state = await player.GetPlaybackAsync(ct).ConfigureAwait(false);
        if (state is { IsPlaying: true })
        {
            await player.PauseAsync(ct).ConfigureAwait(false);
            return PlayPauseOutcome.Paused;
        }

        if (state is { Title: not null })
        {
            await player.ResumeAsync(ct).ConfigureAwait(false);
            return PlayPauseOutcome.Resumed;
        }

        var devices = await player.GetDevicesAsync(ct).ConfigureAwait(false);
        var device = DeviceConnector.FindByName(devices, deviceName);
        if (device is null || device.IsRestricted || string.IsNullOrEmpty(device.Id))
        {
            log?.Invoke($"play: nothing loaded and \"{deviceName}\" is not available");
            return PlayPauseOutcome.NoDevice;
        }

        if (!string.IsNullOrWhiteSpace(defaultContextUri))
        {
            log?.Invoke($"play: starting {defaultContextUri} on \"{device.Name}\"");
            await player.PlayContextAsync(defaultContextUri, device.Id, ct).ConfigureAwait(false);
            return PlayPauseOutcome.StartedDefault;
        }

        log?.Invoke($"play: nothing loaded, asking \"{device.Name}\" to resume");
        await player.TransferPlaybackAsync(device.Id, play: true, ct).ConfigureAwait(false);
        return PlayPauseOutcome.Resumed;
    }

    public Task NextAsync(CancellationToken ct = default) => player.NextAsync(ct);

    public Task PreviousAsync(CancellationToken ct = default) => player.PreviousAsync(ct);

    /// <summary>One-line description for the tray tooltip, e.g. "▶ Song — Artist".</summary>
    public static string Describe(PlaybackState? state)
    {
        if (state is null)
        {
            return "no active device";
        }

        if (state.Title is null)
        {
            return $"nothing playing on {state.DeviceName}";
        }

        var symbol = state.IsPlaying ? "▶" : "⏸";
        return string.IsNullOrEmpty(state.Artist)
            ? $"{symbol} {state.Title}"
            : $"{symbol} {state.Title} — {state.Artist}";
    }
}
