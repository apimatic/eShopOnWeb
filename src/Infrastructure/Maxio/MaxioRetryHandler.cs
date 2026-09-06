using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and full jitter.
/// <para>
/// Only requests that are safe to repeat are retried: reads (GET/HEAD) on network errors, timeouts
/// and 5xx/408 responses, and <em>any</em> method on 429, because a throttled request was never
/// processed. Writes are otherwise left alone - their idempotency comes from the unique references
/// the service assigns, not from blind retries.
/// </para>
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryBaseDelayMilliseconds));

        for (var attempt = 1; ; attempt++)
        {
            var isFinalAttempt = attempt >= maxAttempts;

            HttpResponseMessage? response = null;
            TimeSpan delay;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!ShouldRetry(request, response) || isFinalAttempt)
                {
                    return response;
                }

                delay = RetryAfter(response) ?? Backoff(baseDelay, attempt);

                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode}; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}).",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);

                response.Dispose();
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && IsSafeToRepeat(request) && !isFinalAttempt)
            {
                response?.Dispose();
                delay = Backoff(baseDelay, attempt);

                _logger.LogWarning(ex,
                    "Maxio {Method} {Path} failed to complete; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}).",
                    request.Method, request.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        var isTransientStatus = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;

        return isTransientStatus && IsSafeToRepeat(request);
    }

    private static bool IsSafeToRepeat(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            // HttpClient surfaces its own timeout as a cancellation that the caller did not request.
            TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    private static TimeSpan Backoff(TimeSpan baseDelay, int attempt)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, TimeSpan.FromSeconds(10).TotalMilliseconds);
        var jittered = Random.Shared.NextDouble() * capped;

        // Full jitter, floored at half the base delay so a retry never turns into a tight loop.
        return TimeSpan.FromMilliseconds(Math.Max(baseDelay.TotalMilliseconds / 2, jittered));
    }
}
