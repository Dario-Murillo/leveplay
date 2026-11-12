using Leveplay.Core.Config;
using Leveplay.Core.Engine;

namespace Leveplay.Core.Tests;

public sealed class SoloistEngineTests
{
    [Fact]
    public void Arguments_carry_the_device_name_and_key_as_separate_tokens()
    {
        // Separate tokens mean a device name with spaces needs no manual quoting.
        Assert.Equal(
            ["--device-name", "Living room", "--api-key", "key-123"],
            SoloistEngine.BuildArguments("Living room", "key-123"));
    }

    [Fact]
    public void Expired_build_exit_code_tells_the_user_to_update()
    {
        Assert.Contains("expired", SoloistEngine.DescribeExit(SoloistEngine.ExpiredBuildExitCode));
        Assert.Contains("exit code 3", SoloistEngine.DescribeExit(3));
    }

    [Theory]
    [InlineData("2026-11-12 14:14:38.640: playing spotify:track:abc \"Song\" by \"Artist\" [0:38 / 2:20]", false)]
    [InlineData("2026-11-12 14:14:26.488: became active device", true)]
    [InlineData("2026-11-12 14:13:19.827: client expires in 89 days", true)]
    [InlineData("   ", false)]
    public void Progress_lines_are_kept_out_of_the_log(string line, bool expected)
    {
        Assert.Equal(expected, SoloistEngine.IsWorthLogging(line));
    }

    [Theory]
    [InlineData("2026-11-12 14:32:13.253: ready, device_name=Leveplay", "ready, device_name=Leveplay")]
    [InlineData("no timestamp here", "no timestamp here")]
    public void Engine_timestamps_are_stripped_from_log_lines(string line, string expected)
    {
        Assert.Equal(expected, SoloistEngine.StripTimestamp(line));
    }

    [Fact]
    public void Starting_a_missing_executable_fails_with_a_clear_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"leveplay-missing-{Guid.NewGuid():N}.exe");
        using var engine = new SoloistEngine(missing, "Leveplay");

        var ex = Assert.Throws<EngineException>(() => engine.Start("key"));

        Assert.Contains(missing, ex.Message);
        Assert.False(engine.IsRunning);
    }
}

public sealed class EngineLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("leveplay-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Finds_the_engine_in_the_default_folder()
    {
        var exe = Path.Combine(_dir, EngineLocator.ExecutableName);
        File.WriteAllText(exe, "");

        Assert.Equal(exe, EngineLocator.Resolve(configuredPath: null, _dir));
    }

    [Fact]
    public void Reports_not_installed_when_the_default_folder_is_empty_or_missing()
    {
        Assert.Null(EngineLocator.Resolve(null, _dir));
        Assert.Null(EngineLocator.Resolve("  ", Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void A_configured_path_wins_over_the_default_folder()
    {
        File.WriteAllText(Path.Combine(_dir, EngineLocator.ExecutableName), "");

        Assert.Equal(@"D:\custom\soloist.exe", EngineLocator.Resolve(@"D:\custom\soloist.exe", _dir));
    }

    [Fact]
    public void Default_folder_is_under_local_app_data()
    {
        Assert.EndsWith(Path.Combine("Leveplay", "engine"), EngineLocator.DefaultDirectory);
    }
}

public sealed class EngineSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("leveplay-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteLocal(string json) =>
        File.WriteAllText(Path.Combine(_dir, AppSettingsLoader.LocalFileName), json);

    [Fact]
    public void Engine_is_not_managed_by_default()
    {
        WriteLocal("""{ "ClientId": "abc", "EnginePath": "  " }""");

        Assert.Null(AppSettingsLoader.Load(_dir).EnginePath);
    }

    [Fact]
    public void Engine_path_must_exist()
    {
        WriteLocal("""{ "ClientId": "abc", "EnginePath": "C:\\nope\\soloist.exe" }""");

        var ex = Assert.Throws<SettingsException>(() => AppSettingsLoader.Load(_dir));

        Assert.Contains("EnginePath", ex.Message);
    }

    [Fact]
    public void Engine_path_expands_environment_variables()
    {
        var exe = Path.Combine(_dir, "soloist.exe");
        File.WriteAllText(exe, "");
        Environment.SetEnvironmentVariable("LEVEPLAY_TEST_DIR", _dir);
        WriteLocal("""{ "ClientId": "abc", "EnginePath": "%LEVEPLAY_TEST_DIR%\\soloist.exe" }""");

        Assert.Equal(exe, AppSettingsLoader.Load(_dir).EnginePath);
    }

    [Fact]
    public void Secrets_are_stored_in_separate_files()
    {
        new DpapiTokenStore(_dir).Save("refresh");
        new DpapiTokenStore(_dir, DpapiTokenStore.EngineApiKeyFile).Save("api-key");

        Assert.Equal("refresh", new DpapiTokenStore(_dir).Load());
        Assert.Equal("api-key", new DpapiTokenStore(_dir, DpapiTokenStore.EngineApiKeyFile).Load());
    }
}
