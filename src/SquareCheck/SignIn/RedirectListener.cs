using System.Net;
using System.Text;
using System.Web;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>The redirect address could not be listened on (port taken, address not usable, ...).</summary>
public sealed class RedirectListenerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Listens on the registered redirect address for Square's redirect after the merchant approves
/// (or declines). Only a visit carrying this run's <c>state</c> is accepted; anything else gets a
/// "not this sign-in" page and is otherwise ignored.
/// </summary>
public sealed class RedirectListener : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _callbackPath;

    private RedirectListener(HttpListener listener, string callbackPath)
    {
        _listener = listener;
        _callbackPath = callbackPath;
    }

    /// <summary>Starts listening. Call before the browser is sent to Square, so the redirect cannot be missed.</summary>
    public static RedirectListener Start(Uri redirectAddress)
    {
        var prefix = $"{redirectAddress.Scheme}://{redirectAddress.Authority}/";
        var listener = new HttpListener { IgnoreWriteExceptions = true };
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            throw new RedirectListenerException(
                $"Cannot listen for Square's redirect on {prefix} ({ex.Message.TrimEnd('.')}). "
                + "Close the program using that port, or register a different redirect URL with Square and update Square:RedirectUri.",
                ex);
        }

        return new RedirectListener(listener, NormalizePath(redirectAddress.AbsolutePath));
    }

    /// <summary>
    /// Waits for the redirect that answers <paramref name="request"/>. The returned callback holds the
    /// browser's request open so the caller can answer it once it knows the outcome.
    /// </summary>
    public async Task<AuthorizationCallback> WaitForCallbackAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        while (true)
        {
            var pending = _listener.GetContextAsync();
            HttpListenerContext context;
            try
            {
                context = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The pending accept ends when the listener is disposed; observe it so it is not reported as unobserved.
                _ = pending.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                throw;
            }

            var reply = new BrowserReply(context);
            var callback = Classify(context.Request, request);
            if (callback is null)
            {
                await reply.SendAsync(IsCallbackPath(context.Request) ? SignInPages.NotThisSignIn() : SignInPages.NotFound())
                    .ConfigureAwait(false);
                continue;
            }

            return callback(reply);
        }
    }

    public void Dispose() => _listener.Close();

    private Func<BrowserReply, AuthorizationCallback>? Classify(HttpListenerRequest httpRequest, AuthorizationRequest request)
    {
        if (httpRequest.HttpMethod != "GET" || !IsCallbackPath(httpRequest))
        {
            return null;
        }

        var query = HttpUtility.ParseQueryString(httpRequest.Url?.Query ?? string.Empty);

        // A stale tab from an earlier run, or a link from someone else, carries a different (or no) state.
        if (!request.IsOwnState(query["state"]))
        {
            return null;
        }

        if (query["code"] is { Length: > 0 } code)
        {
            return reply => new ApprovedCallback(code, reply);
        }

        if (query["error"] is { Length: > 0 } error)
        {
            var description = query["error_description"];
            return reply => new DeniedCallback(error, description, reply);
        }

        return null;
    }

    private bool IsCallbackPath(HttpListenerRequest httpRequest) =>
        string.Equals(NormalizePath(httpRequest.Url?.AbsolutePath ?? string.Empty), _callbackPath, StringComparison.Ordinal);

    private static string NormalizePath(string path) => "/" + path.Trim('/');
}

/// <summary>Square's redirect for this run.</summary>
public abstract class AuthorizationCallback(BrowserReply reply)
{
    /// <summary>The browser's request, still open, waiting for the page that tells the operator how it went.</summary>
    public BrowserReply Reply { get; } = reply;
}

/// <summary>The merchant approved; <see cref="Code"/> is the single-use authorization code.</summary>
public sealed class ApprovedCallback(string code, BrowserReply reply) : AuthorizationCallback(reply)
{
    public string Code { get; } = code;
}

/// <summary>Square redirected back with an error instead of a code.</summary>
public sealed class DeniedCallback(string error, string? description, BrowserReply reply) : AuthorizationCallback(reply)
{
    public string Error { get; } = error;
    public string? Description { get; } = description;

    /// <summary>The operator chose not to grant access (OAuth 2.0 <c>access_denied</c>).</summary>
    public bool OperatorDeclined => string.Equals(Error, "access_denied", StringComparison.Ordinal);
}

/// <summary>Answers one browser request, once. Failures to reach a closed tab are ignored.</summary>
public sealed class BrowserReply(HttpListenerContext context)
{
    private int _sent;

    public async Task SendAsync(SignInPage page)
    {
        if (Interlocked.Exchange(ref _sent, 1) == 1)
        {
            return;
        }

        var response = context.Response;
        try
        {
            var body = Encoding.UTF8.GetBytes(page.Html);
            response.StatusCode = (int)page.Status;
            response.ContentType = "text/html; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The tab was closed or the listener is shutting down; the terminal still tells the operator.
            try { response.Abort(); } catch (ObjectDisposedException) { }
        }
    }
}
