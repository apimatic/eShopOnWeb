using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries transient Maxio failures with exponential back-off and jitter, honouring <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// Server-side faults (5xx, timeouts, socket errors) are only retried for read-only methods, because a
/// non-idempotent request such as <c>POST /subscriptions.json</c> may well have been applied before the
/// failure surfaced. <c>429 Too Many Requests</c> is retried for every method: a throttled request was
/// rejected before it was processed, so replaying it cannot duplicate anything.
/// </remarks>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> RetryableServerStatuses = new()
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = Math.Max(1, settings.MaxRetryAttempts + 1);
        var isSafeToReplay = IsSafeToReplay(request.Method);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!ShouldRetry(response.StatusCode, isSafeToReplay))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (isSafeToReplay)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (isSafeToReplay && !cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation that the caller did not request.
                transportFailure = ex;
            }

            if (attempt >= maxAttempts)
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var delay = ComputeDelay(attempt, response, settings);

            _logger.LogWarning(
                transportFailure,
                "Maxio request {Method} {Path} failed transiently ({Outcome}); retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is null ? "transport error" : ((int)response.StatusCode).ToString(),
                (int)delay.TotalMilliseconds,
                attempt,
                maxAttempts);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsSafeToReplay(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isSafeToReplay) =>
        statusCode == HttpStatusCode.TooManyRequests || (isSafeToReplay && RetryableServerStatuses.Contains(statusCode));

    private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response, MaxioSettings settings)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            var hinted = retryAfter.Delta
                         ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);

            if (hinted is { } wait && wait > TimeSpan.Zero)
            {
                return wait > MaxBackoff ? MaxBackoff : wait;
            }
        }

        var baseDelayMs = Math.Max(1, settings.RetryBaseDelayMilliseconds);
        var exponentialMs = baseDelayMs * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.NextDouble() * baseDelayMs;
        var totalMs = Math.Min(exponentialMs + jitterMs, MaxBackoff.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(totalMs);
    }
}
