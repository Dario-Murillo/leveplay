using Leveplay.Core.Config;

namespace Leveplay.Core.Tests;

public sealed class AppSettingsLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("leveplay-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string fileName, string json) => File.WriteAllText(Path.Combine(_dir, fileName), json);

    [Fact]
    public void Local_file_overrides_base_file_and_keeps_other_values()
    {
        Write(AppSettingsLoader.BaseFileName, """{ "ClientId": "", "DeviceName": "Base", "RedirectPort": 6000 }""");
        Write(AppSettingsLoader.LocalFileName, """{ "ClientId": "abc123", "DeviceName": "Local" }""");

        var settings = AppSettingsLoader.Load(_dir);

        Assert.Equal("abc123", settings.ClientId);
        Assert.Equal("Local", settings.DeviceName);
        Assert.Equal(6000, settings.RedirectPort);
        Assert.Equal(new Uri("http://127.0.0.1:6000/callback"), settings.RedirectUri);
    }

    [Fact]
    public void Defaults_apply_when_only_client_id_is_given()
    {
        Write(AppSettingsLoader.LocalFileName, """
            {
              // comments and trailing commas are allowed
              "clientId": "abc123",
            }
            """);

        var settings = AppSettingsLoader.Load(_dir);

        Assert.Equal(5543, settings.RedirectPort);
        Assert.Equal("Leveplay", settings.DeviceName);
        Assert.Equal(5, settings.PollIntervalSeconds);
        Assert.Null(settings.DefaultContextUri);
    }

    [Fact]
    public void Missing_client_id_is_reported_clearly()
    {
        Write(AppSettingsLoader.BaseFileName, """{ "ClientId": "" }""");

        var ex = Assert.Throws<SettingsException>(() => AppSettingsLoader.Load(_dir));

        Assert.Contains("ClientId", ex.Message);
        Assert.Contains(AppSettingsLoader.LocalFileName, ex.Message);
    }

    [Fact]
    public void Malformed_json_is_reported_with_the_file_name()
    {
        Write(AppSettingsLoader.LocalFileName, "{ not json");

        var ex = Assert.Throws<SettingsException>(() => AppSettingsLoader.Load(_dir));

        Assert.Contains(AppSettingsLoader.LocalFileName, ex.Message);
    }

    [Fact]
    public void Out_of_range_port_is_rejected()
    {
        Write(AppSettingsLoader.LocalFileName, """{ "ClientId": "abc123", "RedirectPort": 80 }""");

        Assert.Throws<SettingsException>(() => AppSettingsLoader.Load(_dir));
    }
}
