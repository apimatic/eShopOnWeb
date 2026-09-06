using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The one place eShopOnWeb sees raw Maxio HTTP traffic. It does two load-bearing jobs plus optional logging.
/// </summary>
/// <remarks>
/// <para>
/// <b>Write-once.</b> The SDK's retry pipeline retries a transport failure (a connection reset, a dropped
/// socket) on <em>every</em> verb, regardless of which HTTP methods are configured for status retries, and
/// retries cannot be switched off. A reset thrown after the bytes reached Maxio is indistinguishable from one
/// thrown before, so without this guard a single "Subscribe" click could create two customers or two
/// subscriptions. Inside a write-once scope this handler lets exactly one send reach the network and refuses
/// the rest; the caller then reconciles by re-reading Maxio rather than assuming either outcome.
/// </para>
/// <para>
/// <b>Status capture.</b> When a response body does not match the shape the SDK expects, the SDK throws a
/// <see cref="System.Text.Json.JsonException"/> while building its error object — destroying the HTTP status
/// with it. Recording the status here lets the integration boundary still tell a deterministic rejection
/// (which must not be retried) from a genuinely unknown outcome.
/// </para>
/// </remarks>
internal sealed class MaxioHttpDiagnosticsHandler : DelegatingHandler
{
    private readonly ILogger<MaxioHttpDiagnosticsHandler> _logger;
    private readonly MaxioSettings _settings;

    public MaxioHttpDiagnosticsHandler(IOptions<MaxioSettings> settings, ILogger<MaxioHttpDiagnosticsHandler> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = MaxioCallScope.Current;

        if (scope is not null)
        {
            var sends = Interlocked.Increment(ref scope.Sends);

            if (scope.WriteOnce && sends > 1)
            {
                _logger.LogWarning(
                    "Refused re-send #{Send} of {Method} {Path} to Maxio: the first send may already have taken effect.",
                    sends, request.Method, request.RequestUri?.AbsolutePath);

                throw new MaxioWriteBlockedException(
                    $"Refused to re-send {request.Method} {request.RequestUri?.AbsolutePath} to Maxio.");
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (scope is not null)
        {
            scope.LastStatusCode = (int)response.StatusCode;
        }

        if (_settings.LogRequests)
        {
            _logger.LogDebug("Maxio {Method} {Path} -> {StatusCode}",
                request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode);
        }

        return response;
    }
}
