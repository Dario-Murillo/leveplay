using Leveplay.Core.Config;
using SpotifyAPI.Web;

namespace Leveplay.Core.Auth;

/// <summary>
/// Signs in with the Authorization Code + PKCE flow. The first run opens the browser;
/// later runs reuse the stored refresh token and need no interaction.
/// </summary>
public sealed class SpotifyAuthService(AppSettings settings, ITokenStore tokenStore, Action<Uri> openBrowser)
{
    private static readonly string[] RequiredScopes =
    [
        Scopes.UserReadPlaybackState,
        Scopes.UserModifyPlaybackState,
        Scopes.UserReadCurrentlyPlaying,
    ];

    /// <summary>How long to wait for the user to finish the browser flow.</summary>
    public TimeSpan InteractiveTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public async Task<SpotifyClient> SignInAsync(CancellationToken ct = default)
    {
        var token = await TryRefreshAsync(ct).ConfigureAwait(false)
                    ?? await AuthorizeInteractivelyAsync(ct).ConfigureAwait(false);
        Persist(token);

        // PKCE refresh tokens rotate: every refresh returns a new one that replaces the old.
        var authenticator = new PKCEAuthenticator(settings.ClientId, token);
        authenticator.TokenRefreshed += (_, refreshed) => Persist(refreshed);

        return new SpotifyClient(SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));
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
            return await new OAuthClient()
                .RequestToken(new PKCETokenRefreshRequest(settings.ClientId, refreshToken), ct)
                .ConfigureAwait(false);
        }
        catch (APIException)
        {
            // Revoked or expired refresh token: fall back to the browser flow.
            // Network failures are not APIException and deliberately propagate,
            // so being offline never throws away a valid token.
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

    private void Persist(PKCETokenResponse token)
    {
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            tokenStore.Save(token.RefreshToken);
        }
    }
}
