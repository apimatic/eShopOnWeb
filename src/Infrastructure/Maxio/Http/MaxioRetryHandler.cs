using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Applies a per-attempt timeout and retries transient Maxio failures with exponential backoff and
/// full jitter.
/// <para>
/// What is retried is deliberately conservative, because Maxio has no request-deduplication header:
/// </para>
/// <list type="bullet">
///   <item>429 - retried for any method. The request was rate limited, not processed. A
///   <c>Retry-After</c> header, when present, wins over the computed backoff.</item>
///   <item>5xx and transport faults - retried for safe methods (GET/HEAD) only. Replaying a POST
///   that may already have created a customer or a subscription would be worse than failing, so
///   POSTs surface the error and let the caller's idempotency check resolve it.</item>
/// </list>
/// </summary>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = Math.Max(settings.MaxRetryAttempts, 0) + 1;
        var canRetryFailures = IsSafeMethod(request.Method);

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;
            HttpResponseMessage? response = null;

            try
            {
                response = await SendWithTimeoutAsync(request, settings.RequestTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (isLastAttempt || !ShouldRetry(response.StatusCode, canRetryFailures))
                {
                    return response;
                }

                var delay = GetRetryAfter(response) ?? ComputeBackoff(settings, attempt);
                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode}; retrying in {DelayMs} ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    (int)response.StatusCode,
                    (int)delay.TotalMilliseconds,
                    attempt,
                    maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientTransportFault(ex, cancellationToken))
            {
                response?.Dispose();

                if (isLastAttempt || !canRetryFailures)
                {
                    throw;
                }

                var delay = ComputeBackoff(settings, attempt);
                _logger.LogWarning(
                    ex,
                    "Maxio {Method} {Path} failed in transport; retrying in {DelayMs} ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds,
                    attempt,
                    maxAttempts);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await base.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The Maxio request timed out after {timeout.TotalSeconds:0.#} seconds.");
        }
    }

    private static bool IsSafeMethod(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head;

    private static bool ShouldRetry(HttpStatusCode statusCode, bool canRetryFailures)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return canRetryFailures && (int)statusCode >= 500;
    }

    private static bool IsTransientTransportFault(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or TimeoutException;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Cap(delta);
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Cap(wait);
            }
        }

        return null;
    }

    private static TimeSpan Cap(TimeSpan delay) => delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;

    /// <summary>Exponential backoff with full jitter, which spreads out simultaneous retries.</summary>
    private static TimeSpan ComputeBackoff(MaxioSettings settings, int attempt)
    {
        var baseDelay = settings.RetryBaseDelay;
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, TimeSpan.FromSeconds(10).TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped);
    }
}
