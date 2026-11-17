using Leveplay.Core.Config;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace Leveplay.Core.Auth;

/// <summary>
/// An authenticated connection to Spotify. Both members share one authenticator, so the
/// access token is refreshed once for the two of them.
/// </summary>
/// <param name="Client">Typed client, used for the player endpoints.</param>
/// <param name="Api">Raw connector, for endpoints the typed client no longer matches.</param>
public sealed record SpotifySession(ISpotifyClient Client, IAPIConnector Api);

/// <summary>
/// Signs in with the Authorization Code + PKCE flow. The first run opens the browser;
/// later runs reuse the stored refresh token and need no interaction.
/// </summary>
/// <param name="refresh">Replaces the token refresh call in tests.</param>
public sealed class SpotifyAuthService(
    AppSettings settings,
    ITokenStore tokenStore,
    Action<Uri> openBrowser,
    Func<PKCETokenRefreshRequest, CancellationToken, Task<PKCETokenResponse>>? refresh = null)
{
    /// <summary>How long to wait for the user to finish the browser flow.</summary>
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(3);

    private static readonly string[] RequiredScopes =
    [
        Scopes.UserReadPlaybackState,
        Scopes.UserModifyPlaybackState,
        Scopes.UserReadCurrentlyPlaying,
        Scopes.PlaylistReadPrivate,
        Scopes.PlaylistReadCollaborative,
    ];

    public async Task<SpotifySession> SignInAsync(CancellationToken ct = default)
    {
        var token = await TryRefreshAsync(ct).ConfigureAwait(false)
                    ?? await AuthorizeInteractivelyAsync(ct).ConfigureAwait(false);
        Persist(token);

        // PKCE refresh tokens rotate: every refresh returns a new one that replaces the old.
        var authenticator = new PKCEAuthenticator(settings.ClientId, token);
        authenticator.TokenRefreshed += (_, refreshed) => Persist(refreshed);

        var config = SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator);
        return new SpotifySession(new SpotifyClient(config), config.BuildAPIConnector());
    }

    public void SignOut() => tokenStore.Clear();

    private async Task<PKCETokenResponse?> TryRefreshAsync(CancellationToken ct)
    {
        if (tokenStore.Load() is not { } refreshToken)
        {
            return null;
        }

        try
        {
            var request = new PKCETokenRefreshRequest(settings.ClientId, refreshToken);
            var token = await (refresh?.Invoke(request, ct) ?? new OAuthClient().RequestToken(request, ct))
                .ConfigureAwait(false);

            // Keep the rotated token even if we are about to ask for consent again, so
            // cancelling the browser flow does not sign the user out.
            Persist(token);

            // A token granted by an older version lacks scopes added since then. The
            // only way to get them is a new consent screen.
            return HasRequiredScopes(token.Scope) ? token : null;
        }
        catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // 400 is Spotify's answer for a revoked or expired refresh token: fall back
            // to the browser flow. Everything else (network failures, 5xx, 429)
            // deliberately propagates, so an outage never throws away a valid token.
            tokenStore.Clear();
            return null;
        }
    }

    private async Task<PKCETokenResponse> AuthorizeInteractivelyAsync(CancellationToken ct)
    {
        var (verifier, challenge) = PKCEUtil.GenerateCodes();
        var state = Guid.NewGuid().ToString("N");

        var login = new LoginRequest(settings.RedirectUri, settings.ClientId, LoginRequest.ResponseType.Code)
        {
            CodeChallengeMethod = "S256",
            CodeChallenge = challenge,
            State = state,
            Scope = RequiredScopes,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InteractiveTimeout);

        // Start listening before opening the browser so the redirect cannot arrive early.
        var callbackTask = LoopbackCallbackListener.WaitForCallbackAsync(settings.RedirectUri, timeout.Token);
        openBrowser(login.ToUri());

        Uri callback;
        try
        {
            callback = await callbackTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AuthException("Sign-in timed out waiting for the browser.");
        }

        var code = AuthCallback.ExtractCode(callback, state);
        return await new OAuthClient()
            .RequestToken(new PKCETokenRequest(settings.ClientId, code, settings.RedirectUri, verifier), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// True when a space-separated scope list covers everything Leveplay needs. An empty
    /// list is accepted: without it there is nothing to compare, and asking for consent
    /// on every start would be worse than a late permission error.
    /// </summary>
    public static bool HasRequiredScopes(string? grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes))
        {
            return true;
        }

        var granted = grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return RequiredScopes.All(required => granted.Contains(required, StringComparer.Ordinal));
    }

    private void Persist(PKCETokenResponse token)
    {
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            tokenStore.Save(token.RefreshToken);
        }
    }
}
