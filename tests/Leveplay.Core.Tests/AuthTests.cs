using System.Net;
using Leveplay.Core.Auth;
using Leveplay.Core.Config;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

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

public sealed class SpotifyAuthServiceTests
{
    private sealed class MemoryTokenStore : ITokenStore
    {
        public string? Token { get; set; } = "saved-token";

        public string? Load() => Token;

        public void Save(string refreshToken) => Token = refreshToken;

        public void Clear() => Token = null;
    }

    private sealed record Response(HttpStatusCode StatusCode) : IResponse
    {
        public object? Body => null;

        public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public string? ContentType => null;
    }

    private static SpotifyAuthService Service(MemoryTokenStore store, Exception refreshFailure, Action<Uri> openBrowser) =>
        new(new AppSettings { ClientId = "client", RedirectPort = 5597 }, store, openBrowser,
            (_, _) => Task.FromException<PKCETokenResponse>(refreshFailure));

    [Fact]
    public async Task A_network_error_keeps_the_saved_token()
    {
        var store = new MemoryTokenStore();
        var auth = Service(store, new HttpRequestException("offline"), _ => Assert.Fail("opened the browser"));

        await Assert.ThrowsAsync<HttpRequestException>(() => auth.SignInAsync());

        Assert.Equal("saved-token", store.Token);
    }

    [Fact]
    public async Task A_server_error_keeps_the_saved_token()
    {
        var store = new MemoryTokenStore();
        var failure = new APIException(new Response(HttpStatusCode.ServiceUnavailable));
        var auth = Service(store, failure, _ => Assert.Fail("opened the browser"));

        await Assert.ThrowsAsync<APIException>(() => auth.SignInAsync());

        Assert.Equal("saved-token", store.Token);
    }

    [Fact]
    public async Task A_rejected_token_is_deleted_and_the_browser_flow_starts()
    {
        var store = new MemoryTokenStore();
        using var cts = new CancellationTokenSource();
        var failure = new APIException(new Response(HttpStatusCode.BadRequest));

        // Cancelling as soon as the browser would open ends the flow without a real login.
        var auth = Service(store, failure, _ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => auth.SignInAsync(cts.Token));

        Assert.Null(store.Token);
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
