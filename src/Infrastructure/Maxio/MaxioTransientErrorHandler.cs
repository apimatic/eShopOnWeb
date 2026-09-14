using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff. Maxio throttles by
/// concurrency and answers overload with <c>429</c>, so this handler backs off
/// (honouring <c>Retry-After</c> when present) rather than hammering the API.
/// Only idempotent-safe transient conditions are retried; genuine 4xx validation
/// errors are passed straight through.
/// </summary>
public class MaxioTransientErrorHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,        // 408
        (HttpStatusCode)429,                  // Too Many Requests
        HttpStatusCode.InternalServerError,   // 500
        HttpStatusCode.BadGateway,            // 502
        HttpStatusCode.ServiceUnavailable,    // 503
        HttpStatusCode.GatewayTimeout         // 504
    };

    private readonly IAppLogger<MaxioTransientErrorHandler> _logger;

    public MaxioTransientErrorHandler(IAppLogger<MaxioTransientErrorHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (attempt >= MaxAttempts || !RetryableStatusCodes.Contains(response.StatusCode))
                {
                    return response;
                }

                var delay = GetDelay(response, attempt);
                _logger.LogWarning($"Maxio request to {request.RequestUri} returned {(int)response.StatusCode}; retry {attempt}/{MaxAttempts - 1} after {delay.TotalMilliseconds:n0}ms.");
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                response?.Dispose();
                var delay = GetDelay(null, attempt);
                _logger.LogWarning($"Maxio request to {request.RequestUri} failed ({ex.Message}); retry {attempt}/{MaxAttempts - 1} after {delay.TotalMilliseconds:n0}ms.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return until;
                }
            }
        }

        // Exponential backoff: 0.5s, 1s, 2s ...
        return TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
    }
}
