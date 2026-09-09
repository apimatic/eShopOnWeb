using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures — HTTP 429 (Maxio throttles by concurrency) and 5xx — with bounded
/// exponential backoff and jitter, honouring a Retry-After header when present. POSTs are only retried
/// on 429 (never on 5xx), because a 5xx after a POST may mean the write partially succeeded; the caller's
/// idempotency (reference + uniqueness_token) is what makes retrying writes safe elsewhere.
/// </summary>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 4;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();
            response = await base.SendAsync(request, cancellationToken);

            if (!ShouldRetry(response.StatusCode, request.Method) || attempt >= MaxAttempts)
            {
                return response;
            }

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning(
                "Maxio request {Method} {Path} returned {StatusCode}; retrying (attempt {Attempt}/{MaxAttempts}) after {DelayMs}ms.",
                request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode, attempt, MaxAttempts, (int)delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, HttpMethod method)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // Only retry 5xx for idempotent methods to avoid duplicating a write that may have landed.
        var isServerError = (int)statusCode >= 500;
        var isIdempotent = method == HttpMethod.Get || method == HttpMethod.Head;
        return isServerError && isIdempotent;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        // Exponential backoff with jitter: 0.5s, 1s, 2s (+ up to 250ms random).
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return exponential + jitter;
    }
}
