using System.Net;
using System.Text;

namespace SquareCheck;

// Binds to the redirect URI and waits for a single OAuth callback at a time.
// The connection to the browser is kept open between WaitAsync and RespondAsync,
// allowing the caller to fetch the merchant name before serving the success page.
internal sealed class HttpListenerRedirectReceiver : IRedirectListener
{
    private readonly HttpListener _listener;
    private HttpListenerContext? _currentContext;
    private bool _disposed;

    public HttpListenerRedirectReceiver(string redirectUri)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(BuildPrefix(redirectUri));
        _listener.Start();
    }

    public async Task<OAuthCallback> WaitAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // HttpListener.GetContextAsync() has no built-in cancellation support.
        // Stopping the listener is the only way to unblock it.
        using var reg = ct.Register(static l =>
        {
            try { ((HttpListener)l!).Stop(); } catch { }
        }, _listener);

        try
        {
            _currentContext = await _listener.GetContextAsync();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }

        // After Stop(), Start() is required to accept new connections.
        // Restart here so the loop in OAuthFlowCoordinator can call WaitAsync again
        // for subsequent requests (e.g. stale tabs from previous runs).
        if (!_disposed)
        {
            try { _listener.Start(); } catch { }
        }

        var qs = _currentContext.Request.QueryString;
        return new OAuthCallback(qs["code"], qs["error"], qs["state"]);
    }

    public async Task RespondAsync(int statusCode, string contentType, string body, CancellationToken ct)
    {
        if (_currentContext is null) return;
        var response = _currentContext.Response;
        response.StatusCode = statusCode;
        response.ContentType = $"{contentType}; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, ct);
        response.Close();
        _currentContext = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener.Stop(); } catch { }
        _currentContext?.Response.Abort();
        _currentContext = null;
    }

    private static string BuildPrefix(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        var path = uri.AbsolutePath;
        if (!path.EndsWith('/')) path += '/';
        // Always bind to localhost regardless of what host the redirect URI names.
        return $"{uri.Scheme}://localhost:{uri.Port}{path}";
    }
}
