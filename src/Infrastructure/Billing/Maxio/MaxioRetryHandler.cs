using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// What is retried is deliberately narrow, because a blind retry of a signup would enroll a shopper twice:
/// <list type="bullet">
/// <item>HTTP 429 is retried for any method -- a throttled request was rejected before it was processed.</item>
/// <item>5xx, 408 and network-level faults are retried for GET only, since for those the request may
/// well have been processed and only the response lost.</item>
/// </list>
/// A non-idempotent call that fails this way surfaces to the caller, which then re-reads state from
/// Maxio rather than guessing -- see <see cref="MaxioSubscriptionService"/>.
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _settings.Value;
        var maxAttempts = Math.Max(0, settings.MaxRetryAttempts) + 1;
        var isReadOnly = request.Method == HttpMethod.Get;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode || !ShouldRetryStatus(response.StatusCode, isReadOnly))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (isReadOnly)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (isReadOnly && !cancellationToken.IsCancellationRequested)
            {
                // A per-attempt timeout rather than caller cancellation.
                transportFailure = ex;
            }

            if (attempt >= maxAttempts)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            var delay = ComputeDelay(settings.RetryBaseDelay, attempt, response);
            _logger.LogWarning(
                transportFailure,
                "Maxio call {Method} {Path} failed on attempt {Attempt}/{MaxAttempts} ({Outcome}); retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                response is not null ? ((int)response.StatusCode).ToString() : "transport error",
                delay.TotalMilliseconds);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetryStatus(HttpStatusCode statusCode, bool isReadOnly)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return isReadOnly && (statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500);
    }

    private static TimeSpan ComputeDelay(TimeSpan baseDelay, int attempt, HttpResponseMessage? response)
    {
        // Maxio tells us how long to wait when it throttles; honour that over our own backoff.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(exponential + jitter);
    }
}
