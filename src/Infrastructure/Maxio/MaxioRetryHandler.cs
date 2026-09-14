using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Applies a per-attempt timeout and retries transient Maxio failures with exponential
/// backoff and jitter.
/// <para>
/// Throttling (429) is retried for every verb because the request was provably not processed,
/// and Maxio's Retry-After header is honoured when present. Server-side failures and network
/// faults are only retried for idempotent verbs: a POST that failed after reaching Maxio may
/// well have created a customer or a subscription, so it is left to the caller - which
/// reconciles through the reference lookups - rather than blindly repeated.
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
        var maxAttempts = Math.Max(1, options.MaxRetryAttempts + 1);
        var perAttemptTimeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        var isIdempotent = IsIdempotent(request.Method);

        // Buffer the body so the request can be sent again on a retry.
        if (request.Content is not null && maxAttempts > 1)
        {
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        }

        for (var attempt = 1; ; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(perAttemptTimeout);

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (isIdempotent && attempt < maxAttempts)
            {
                transportFailure = ex;
            }
            catch (OperationCanceledException ex) when (isIdempotent && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                transportFailure = ex;
            }

            if (response is not null)
            {
                if (!ShouldRetry(response.StatusCode, isIdempotent) || attempt >= maxAttempts)
                {
                    return response;
                }

                var delay = GetRetryAfter(response) ?? GetBackoff(options, attempt);
                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode}; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    (int)response.StatusCode,
                    (int)delay.TotalMilliseconds,
                    attempt,
                    maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var backoff = GetBackoff(options, attempt);
            _logger.LogWarning(
                transportFailure,
                "Maxio {Method} {Path} failed to complete; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method,
                request.RequestUri?.AbsolutePath,
                (int)backoff.TotalMilliseconds,
                attempt,
                maxAttempts);

            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isIdempotent) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout => isIdempotent,
        _ => false
    };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    private static TimeSpan GetBackoff(MaxioOptions options, int attempt)
    {
        var baseDelay = Math.Max(1, options.RetryBaseDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay;
        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, 10_000));
    }
}
