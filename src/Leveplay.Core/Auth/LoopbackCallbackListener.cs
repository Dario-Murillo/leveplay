using System.Net;
using System.Text;

namespace Leveplay.Core.Auth;

/// <summary>
/// Minimal one-shot HTTP listener that receives the OAuth redirect on the loopback
/// interface. Loopback prefixes do not need administrator rights or a URL ACL.
/// </summary>
public static class LoopbackCallbackListener
{
    private const string Page =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Leveplay</title></head>" +
        "<body style=\"font-family:Segoe UI,sans-serif;margin:3rem\">" +
        "<h2>Leveplay</h2><p>You can close this tab and return to the app.</p></body></html>";

    public static async Task<Uri> WaitForCallbackAsync(Uri redirectUri, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new AuthException(
                $"Could not listen on {redirectUri} ({ex.Message}). Is another instance running?");
        }

        using var registration = ct.Register(listener.Stop);
        try
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var body = Encoding.UTF8.GetBytes(Page);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
            context.Response.Close();
            return context.Request.Url!;
        }
        catch (Exception ex) when (ct.IsCancellationRequested &&
                                   ex is HttpListenerException or ObjectDisposedException)
        {
            throw new OperationCanceledException(ct);
        }
    }
}
