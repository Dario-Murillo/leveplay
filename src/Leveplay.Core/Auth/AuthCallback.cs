using System.Web;

namespace Leveplay.Core.Auth;

public sealed class AuthException(string message) : Exception(message);

public static class AuthCallback
{
    /// <summary>
    /// Extracts the authorization code from the redirect Spotify sent to the loopback
    /// address, rejecting denied requests and responses whose state does not match.
    /// </summary>
    public static string ExtractCode(Uri callback, string expectedState)
    {
        var query = HttpUtility.ParseQueryString(callback.Query);

        if (query["error"] is { Length: > 0 } error)
        {
            throw new AuthException(error == "access_denied"
                ? "Sign-in was cancelled in the browser."
                : $"Spotify rejected the sign-in: {error}.");
        }

        if (!string.Equals(query["state"], expectedState, StringComparison.Ordinal))
        {
            throw new AuthException("The sign-in response did not match this request (state mismatch).");
        }

        return query["code"] is { Length: > 0 } code
            ? code
            : throw new AuthException("The sign-in response did not include an authorization code.");
    }
}
