using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A small transient-fault retry handler for Maxio calls (no external dependency on Polly).
/// Retries with exponential backoff on connection-level failures for any request, and
/// additionally on retriable status codes (429/502/503/504) for safe (idempotent) methods.
/// Non-idempotent requests (POST) are never retried on a received response so a subscription
/// or customer is never created twice from a single call.
/// </summary>
internal sealed class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> RetriableStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,       // 408
        (HttpStatusCode)429,                 // Too Many Requests
        HttpStatusCode.BadGateway,           // 502
        HttpStatusCode.ServiceUnavailable,   // 503
        HttpStatusCode.GatewayTimeout,       // 504
    };

    private readonly ILogger<MaxioTransientFaultHandler> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    public MaxioTransientFaultHandler(ILogger<MaxioTransientFaultHandler> logger, int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        _logger = logger;
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(300);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isIdempotent = request.Method == HttpMethod.Get ||
                           request.Method == HttpMethod.Head ||
                           request.Method == HttpMethod.Options;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (attempt < _maxRetries && isIdempotent && RetriableStatusCodes.Contains(response.StatusCode))
                {
                    response.Dispose();
                    await DelayAsync(attempt, response.Headers, cancellationToken);
                    _logger.LogWarning(
                        "Retrying Maxio {Method} {Uri} after status {Status} (attempt {Attempt}/{Max}).",
                        request.Method, request.RequestUri, (int)response.StatusCode, attempt + 1, _maxRetries);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                // Connection-level failure: no response was received, so retrying is safe
                // even for POST (the server never processed the request).
                _logger.LogWarning(
                    ex, "Retrying Maxio {Method} {Uri} after transport error (attempt {Attempt}/{Max}).",
                    request.Method, request.RequestUri, attempt + 1, _maxRetries);
                await DelayAsync(attempt, headers: null, cancellationToken);
            }
        }
    }

    private async Task DelayAsync(int attempt, System.Net.Http.Headers.HttpResponseHeaders? headers, CancellationToken cancellationToken)
    {
        // Honor a server-provided Retry-After (seconds) when present, otherwise exponential backoff.
        if (headers?.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            await Task.Delay(delta, cancellationToken);
            return;
        }

        var backoff = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        await Task.Delay(backoff, cancellationToken);
    }
}
