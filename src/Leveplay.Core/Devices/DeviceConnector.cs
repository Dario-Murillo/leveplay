namespace Leveplay.Core.Devices;

public enum ConnectStatus
{
    /// <summary>Playback was transferred to the device.</summary>
    Transferred,

    /// <summary>The device was already the active one; nothing was sent.</summary>
    AlreadyActive,

    /// <summary>No device with the configured name is online.</summary>
    NotFound,

    /// <summary>The device is online but does not accept Web API commands.</summary>
    Restricted,
}

/// <param name="Device">The matched device, or null when <see cref="ConnectStatus.NotFound"/>.</param>
/// <param name="AvailableDeviceNames">Every device that is online, to help the user fix the name.</param>
public sealed record ConnectResult(
    ConnectStatus Status,
    ConnectDevice? Device,
    IReadOnlyList<string> AvailableDeviceNames)
{
    public bool IsConnected => Status is ConnectStatus.Transferred or ConnectStatus.AlreadyActive;
}

/// <summary>Finds the configured Spotify Connect device and makes it the active one.</summary>
public sealed class DeviceConnector(IPlayerGateway player)
{
    public async Task<ConnectResult> ConnectAsync(string deviceName, CancellationToken ct = default)
    {
        var devices = await player.GetDevicesAsync(ct).ConfigureAwait(false);
        var names = devices.Select(d => d.Name).ToList();

        var device = FindByName(devices, deviceName);
        if (device is null)
        {
            return new ConnectResult(ConnectStatus.NotFound, null, names);
        }

        if (device.IsRestricted || string.IsNullOrEmpty(device.Id))
        {
            return new ConnectResult(ConnectStatus.Restricted, device, names);
        }

        if (device.IsActive)
        {
            return new ConnectResult(ConnectStatus.AlreadyActive, device, names);
        }

        await player.TransferPlaybackAsync(device.Id, ct).ConfigureAwait(false);
        return new ConnectResult(ConnectStatus.Transferred, device, names);
    }

    /// <summary>
    /// Matches by name, ignoring case and surrounding whitespace. Device names are not
    /// unique, so when several match, a controllable one wins over a restricted one.
    /// </summary>
    public static ConnectDevice? FindByName(IEnumerable<ConnectDevice> devices, string deviceName)
    {
        var wanted = deviceName.Trim();
        return devices
            .Where(d => string.Equals(d.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.IsRestricted || string.IsNullOrEmpty(d.Id))
            .FirstOrDefault();
    }
}
