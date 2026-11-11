using Leveplay.Core.Auth;
using Leveplay.Core.Config;

namespace Leveplay.Core.Tests;

public sealed class AuthCallbackTests
{
    private static Uri Callback(string query) => new($"http://127.0.0.1:5543/callback{query}");

    [Fact]
    public void Returns_code_when_state_matches()
    {
        Assert.Equal("the-code", AuthCallback.ExtractCode(Callback("?code=the-code&state=s1"), "s1"));
    }

    [Fact]
    public void Rejects_mismatched_state()
    {
        var ex = Assert.Throws<AuthException>(
            () => AuthCallback.ExtractCode(Callback("?code=the-code&state=other"), "s1"));
        Assert.Contains("state", ex.Message);
    }

    [Fact]
    public void Reports_user_cancellation()
    {
        var ex = Assert.Throws<AuthException>(
            () => AuthCallback.ExtractCode(Callback("?error=access_denied&state=s1"), "s1"));
        Assert.Contains("cancelled", ex.Message);
    }

    [Fact]
    public void Rejects_response_without_code()
    {
        Assert.Throws<AuthException>(() => AuthCallback.ExtractCode(Callback("?state=s1"), "s1"));
    }
}

public sealed class LoopbackCallbackListenerTests
{
    [Fact]
    public async Task Returns_the_requested_url_and_serves_a_page()
    {
        var redirect = new Uri("http://127.0.0.1:5599/callback");
        var waiting = LoopbackCallbackListener.WaitForCallbackAsync(redirect, CancellationToken.None);

        using var http = new HttpClient();
        var page = await http.GetStringAsync("http://127.0.0.1:5599/callback?code=abc&state=xyz");
        var callback = await waiting;

        Assert.Contains("close this tab", page);
        Assert.Equal("?code=abc&state=xyz", callback.Query);
    }

    [Fact]
    public async Task Cancellation_stops_listening()
    {
        using var cts = new CancellationTokenSource();
        var waiting = LoopbackCallbackListener.WaitForCallbackAsync(new Uri("http://127.0.0.1:5598/callback"), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}

public sealed class DpapiTokenStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("leveplay-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Round_trips_the_token_without_storing_it_in_plain_text()
    {
        var store = new DpapiTokenStore(_dir);
        Assert.Null(store.Load());

        store.Save("refresh-token-value");

        Assert.Equal("refresh-token-value", new DpapiTokenStore(_dir).Load());
        var raw = File.ReadAllBytes(Path.Combine(_dir, "token.bin"));
        Assert.DoesNotContain("refresh-token-value", System.Text.Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public void Clear_removes_the_token_and_corrupt_files_read_as_signed_out()
    {
        var store = new DpapiTokenStore(_dir);
        store.Save("abc");
        store.Clear();
        Assert.Null(store.Load());

        File.WriteAllBytes(Path.Combine(_dir, "token.bin"), [1, 2, 3]);
        Assert.Null(store.Load());
    }
}
