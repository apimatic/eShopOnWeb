using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Retries transient Walmart failures (HTTP 429 and 5xx, and transport-level
/// <see cref="HttpRequestException"/>/timeouts) with bounded exponential backoff. Logs only the
/// method, URI and status — never request/response bodies or headers, which could carry the
/// client secret or an access token.
/// </summary>
internal sealed class WalmartRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private readonly ILogger<WalmartRetryHandler> _logger;

    public WalmartRetryHandler(ILogger<WalmartRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                response?.Dispose();
                response = await base.SendAsync(request, cancellationToken);

                if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                {
                    return response;
                }

                _logger.LogWarning("Walmart call {Method} {Uri} returned {Status}; retry {Attempt}/{Max}.",
                    request.Method, request.RequestUri, (int)response.StatusCode, attempt, MaxAttempts);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Walmart call {Method} {Uri} failed at transport level; retry {Attempt}/{Max}.",
                    request.Method, request.RequestUri, attempt, MaxAttempts);
            }
            catch (TaskCanceledException ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Walmart call {Method} {Uri} timed out; retry {Attempt}/{Max}.",
                    request.Method, request.RequestUri, attempt, MaxAttempts);
            }

            var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
