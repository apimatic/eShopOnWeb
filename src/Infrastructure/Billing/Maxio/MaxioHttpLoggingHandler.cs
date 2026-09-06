using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Logs one line per Maxio request/response. Off unless <c>Maxio:LogHttpTraffic</c> is true.
/// Verb, path, query, status and elapsed time only - never headers (which carry the API key) and
/// never bodies (which carry customer data). Useful on the first run of a new call to confirm the
/// verb, the substituted path and the query string that actually went out.
/// </summary>
internal sealed class MaxioHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioHttpLoggingHandler> _logger;

    public MaxioHttpLoggingHandler(ILogger<MaxioHttpLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Maxio {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs}ms",
                request.Method,
                request.RequestUri?.AbsolutePath,
                request.RequestUri?.Query,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Maxio {Method} {Path} failed after {ElapsedMs}ms: {ExceptionType}",
                request.Method,
                request.RequestUri?.AbsolutePath,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name);
            throw;
        }
    }
}
