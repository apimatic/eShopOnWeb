using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// Safe-by-default: reads are retried on any transient condition, while writes are retried only when
/// the response proves the request was never processed (429 rate limiting, or a gateway-level 502/503/504).
/// A 500 or a dropped connection on a POST is never retried, because it may have created a customer
/// or a subscription; the caller's lookup-before-create pass is what recovers from those.
/// </remarks>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var maxAttempts = Math.Max(0, settings.MaxRetryAttempts);
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, settings.RetryBaseDelayMilliseconds));
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (attempt >= maxAttempts || !ShouldRetry(response.StatusCode, isRead))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (isRead && attempt < maxAttempts)
            {
                transportFailure = ex;
            }

            var delay = response is not null
                ? DelayFor(response, attempt, baseDelay)
                : Backoff(attempt, baseDelay);

            _logger.LogWarning(
                "Retrying Maxio {Method} {Path} in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}); reason: {Reason}",
                request.Method,
                request.RequestUri?.AbsolutePath,
                (int)delay.TotalMilliseconds,
                attempt + 1,
                maxAttempts,
                response is not null ? $"HTTP {(int)response.StatusCode}" : transportFailure?.GetType().Name);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRead)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            // The request was rejected outright, so replaying it cannot duplicate work.
            return true;
        }

        if (statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        return isRead && statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError;
    }

    private static TimeSpan DelayFor(HttpResponseMessage response, int attempt, TimeSpan baseDelay)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxDelay);
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Min(wait, MaxDelay);
            }
        }

        return Backoff(attempt, baseDelay);
    }

    private static TimeSpan Backoff(int attempt, TimeSpan baseDelay)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;
        return Min(TimeSpan.FromMilliseconds(exponential + jitter), MaxDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
