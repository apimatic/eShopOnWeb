using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// Listens at the registered redirect address (on this computer only) for the browser coming back from
/// Square's sign-in page. Only a visit carrying this run's <c>state</c> can complete the sign-in, and only
/// the first such visit counts. The browser's request for an approved sign-in is held open until the tool
/// knows which business it connected to, so the page can name it.
/// </summary>
public sealed class OAuthCallbackListener : IAsyncDisposable
{
    private static readonly TimeSpan DefaultPageHoldTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly WebApplication _app;
    private readonly PathString _callbackPath;
    private readonly byte[] _expectedState;
    private readonly TimeSpan _pageHoldTimeout;
    private readonly TaskCompletionSource<SignInCallback> _callback =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<BrowserPage> _finalPage =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private bool _received;
    private bool _closed;

    private OAuthCallbackListener(WebApplication app, PathString callbackPath, string expectedState, TimeSpan pageHoldTimeout)
    {
        _app = app;
        _callbackPath = callbackPath;
        _expectedState = Encoding.UTF8.GetBytes(expectedState);
        _pageHoldTimeout = pageHoldTimeout;
    }

    /// <summary>
    /// The address the listener actually bound (resolves port 0 to the assigned port).
    /// </summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>
    /// The full callback address the listener answers at.
    /// </summary>
    public Uri CallbackUri => new(BaseAddress, _callbackPath.Value);

    /// <summary>
    /// Binds the host and port of <paramref name="redirectUri"/> on the loopback interface.
    /// </summary>
    /// <exception cref="RedirectAddressUnavailableException">The port is already in use, or cannot be bound.</exception>
    public static async Task<OAuthCallbackListener> StartAsync(
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken,
        TimeSpan? pageHoldTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrEmpty(expectedState);

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        // Ctrl+C belongs to the tool, not to the embedded web host.
        builder.Services.AddSingleton<IHostLifetime, NoopHostLifetime>();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 0;
            ConfigureEndpoint(options, redirectUri);
        });

        var app = builder.Build();
        var path = new PathString(string.IsNullOrEmpty(redirectUri.AbsolutePath) ? "/" : Uri.UnescapeDataString(redirectUri.AbsolutePath));
        var listener = new OAuthCallbackListener(app, path, expectedState, pageHoldTimeout ?? DefaultPageHoldTimeout);
        app.Run(listener.HandleAsync);

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new RedirectAddressUnavailableException(redirectUri, ex);
        }

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        listener.BaseAddress = address is null
            ? new Uri(redirectUri.GetLeftPart(UriPartial.Authority))
            : new Uri(address.Replace("://[::]", "://localhost", StringComparison.Ordinal));
        return listener;
    }

    /// <summary>
    /// Completes when the first visit carrying this run's state arrives.
    /// </summary>
    public Task<SignInCallback> WaitForCallbackAsync(CancellationToken cancellationToken) =>
        _callback.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Sets the page the held browser request receives, and stops accepting sign-ins.
    /// Only the first call has an effect.
    /// </summary>
    public void Complete(BrowserPage page)
    {
        lock (_gate)
        {
            _closed = true;
        }

        _finalPage.TrySetResult(page);
    }

    public async ValueTask DisposeAsync()
    {
        Complete(BrowserPage.Expired);
        using var stop = new CancellationTokenSource(StopTimeout);
        try
        {
            await _app.StopAsync(stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping is best-effort and bounded; the process is about to end.
        }

        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || !context.Request.Path.Equals(_callbackPath))
        {
            await WritePageAsync(context, BrowserPage.NotFound).ConfigureAwait(false);
            return;
        }

        var query = context.Request.Query;
        if (!StateMatches(query["state"]))
        {
            await WritePageAsync(context, BrowserPage.NotThisSignIn).ConfigureAwait(false);
            return;
        }

        BrowserPage? refusal = null;
        lock (_gate)
        {
            if (_received || _closed)
            {
                refusal = _received ? BrowserPage.AlreadyHandled : BrowserPage.Expired;
            }
            else
            {
                _received = true;
            }
        }

        if (refusal is not null)
        {
            await WritePageAsync(context, refusal).ConfigureAwait(false);
            return;
        }

        var callback = new SignInCallback(
            Single(query["code"]),
            Single(query["error"]),
            Single(query["error_description"]));
        _callback.TrySetResult(callback);

        if (!callback.Approved)
        {
            await WritePageAsync(context, BrowserPage.Declined).ConfigureAwait(false);
            return;
        }

        BrowserPage finalPage;
        try
        {
            finalPage = await _finalPage.Task.WaitAsync(_pageHoldTimeout, context.RequestAborted).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            finalPage = BrowserPage.Failed;
        }
        catch (OperationCanceledException)
        {
            return; // The browser went away.
        }

        await WritePageAsync(context, finalPage).ConfigureAwait(false);
    }

    private bool StateMatches(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(values[0]!), _expectedState);
    }

    private static string? Single(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Count == 1 && !string.IsNullOrEmpty(values[0]) ? values[0] : null;

    private static async Task WritePageAsync(HttpContext context, BrowserPage page)
    {
        var response = context.Response;
        response.StatusCode = page.StatusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        // The callback URL carries the authorization code: never let it leak through a Referer header.
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        await response.WriteAsync(page.ToHtml(), context.RequestAborted).ConfigureAwait(false);
    }

    private static void ConfigureEndpoint(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, Uri redirectUri)
    {
        var port = redirectUri.Port;
        var host = redirectUri.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var address))
        {
            options.Listen(address, port);
        }
        else if (port == 0)
        {
            // Kestrel cannot bind "localhost" to a dynamic port; use the IPv4 loopback instead.
            options.Listen(IPAddress.Loopback, 0);
        }
        else
        {
            // Both 127.0.0.1 and ::1, so whichever "localhost" the browser resolves reaches us.
            options.ListenLocalhost(port);
        }
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// The redirect address could not be listened on (typically: another program already uses the port).
/// </summary>
public sealed class RedirectAddressUnavailableException(Uri redirectUri, Exception innerException)
    : Exception($"Cannot listen at the redirect address {redirectUri.GetLeftPart(UriPartial.Authority)}: " +
                $"port {redirectUri.Port} is unavailable (is another program using it?).", innerException)
{
    public Uri RedirectUri { get; } = redirectUri;
}
