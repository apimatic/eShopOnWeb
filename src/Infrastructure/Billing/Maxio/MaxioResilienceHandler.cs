using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caps concurrency towards Advanced Billing and retries reads that fail transiently.
/// </summary>
/// <remarks>
/// <para>
/// Advanced Billing throttles per site, so a burst of parallel calls earns 429s that are more expensive
/// than simply waiting our turn. The semaphore turns that into ordinary backpressure.
/// </para>
/// <para>
/// Only safe methods are retried. A retried <c>POST</c> could enroll a shopper twice if the first attempt
/// actually landed and only the response was lost, so replay of writes is handled a layer up, where the
/// deterministic reference makes a duplicate detectable — see
/// <see cref="MaxioSubscriptionBillingService"/>.
/// </para>
/// </remarks>
internal sealed class MaxioResilienceHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,          // 408
        (HttpStatusCode)429,                    // Too Many Requests
        HttpStatusCode.InternalServerError,     // 500
        HttpStatusCode.BadGateway,              // 502
        HttpStatusCode.ServiceUnavailable,      // 503
        HttpStatusCode.GatewayTimeout,          // 504
    };

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _concurrencyGate;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(
        int maxConcurrentRequests,
        int maxRetries,
        TimeSpan baseDelay,
        ILogger<MaxioResilienceHandler> logger)
    {
        _concurrencyGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _maxRetries = maxRetries;
        _baseDelay = baseDelay;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var attemptsAllowed = IsRetryable(request.Method) ? _maxRetries : 0;

        // The caller owns the message it handed us; anything we build for a retry is ours to clean up.
        var requestIsOurs = false;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation that the caller did not ask for.
                transportFailure = ex;
            }
            finally
            {
                _concurrencyGate.Release();
            }

            if (transportFailure is null && !RetryableStatusCodes.Contains(response!.StatusCode))
            {
                return response;
            }

            if (attempt >= attemptsAllowed)
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var delay = ComputeDelay(attempt, response);
            _logger.LogWarning(
                "Maxio {Method} {Path} failed ({Outcome}); retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method,
                request.RequestUri?.AbsolutePath,
                transportFailure?.GetType().Name ?? ((int)response!.StatusCode).ToString(),
                (int)delay.TotalMilliseconds,
                attempt + 1,
                attemptsAllowed);

            response?.Dispose();

            // A request message can only be sent once, so rebuild it before trying again.
            var next = await CloneAsync(request).ConfigureAwait(false);

            if (requestIsOurs)
            {
                request.Dispose();
            }

            request = next;
            requestIsOurs = true;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        // Advanced Billing tells us how long to wait when it throttles; prefer that over guessing.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return Min(delta, MaxBackoff);
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return Min(until, MaxBackoff);
                }
            }
        }

        var exponential = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));

        // Full jitter, so concurrent callers that were throttled together do not retry in lockstep.
        var jittered = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * exponential.TotalMilliseconds);
        return Min(jittered, MaxBackoff);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var buffered = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(buffered);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _concurrencyGate.Dispose();
        }

        base.Dispose(disposing);
    }
}
