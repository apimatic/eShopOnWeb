using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// Retries Maxio requests that are safe to repeat, with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// <para>
/// Maxio throttles at the site level and answers HTTP 429 without rate-limit headers, so backoff
/// has to be client-side. A 429 means the request was rejected before it was processed, so it is
/// safe to repeat for any method. A 5xx or a connection failure, by contrast, leaves the outcome of
/// a write unknown — those are only retried for GET.
/// </para>
/// <para>
/// This is not the only protection against duplicate writes: every record this integration creates
/// carries a deterministic reference that Maxio enforces as unique, so a write that did land is
/// rejected on the repeat and resolved by looking the record up.
/// </para>
/// </remarks>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly int _maxAttempts;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(int maxRetryAttempts, ILogger<MaxioRetryHandler> logger)
    {
        // One initial attempt, plus the configured number of retries.
        _maxAttempts = Math.Max(0, maxRetryAttempts) + 1;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body once so every attempt can send an identical, independent copy.
        var body = await BufferContentAsync(request, cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= _maxAttempts;

            // Not disposed on the success path: the caller still has to read the response body, and
            // the request must outlive that. Only a request we are about to abandon is disposed.
            var attemptRequest = Clone(request, body);

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

                if (isLastAttempt || !ShouldRetry(request.Method, response.StatusCode))
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransportFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                if (isLastAttempt || !IsIdempotent(request.Method))
                {
                    attemptRequest.Dispose();
                    throw;
                }

                transportFailure = ex;
            }

            attemptRequest.Dispose();

            var delay = response is not null
                ? GetDelay(attempt, response)
                : GetBackoff(attempt);

            _logger.LogWarning(
                "Maxio {Method} {Path} attempt {Attempt}/{MaxAttempts} failed ({Outcome}); retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                _maxAttempts,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                (int)delay.TotalMilliseconds);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A 429 was never processed, so any method may repeat it; 5xx only for GET.</summary>
    private static bool ShouldRetry(HttpMethod method, HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return (int)statusCode >= 500 && IsIdempotent(method);
    }

    private static bool IsIdempotent(HttpMethod method) => method == HttpMethod.Get;

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is TimeoutException;

    /// <summary>Honours <c>Retry-After</c> when Maxio sends one, otherwise backs off exponentially.</summary>
    private static TimeSpan GetDelay(int attempt, HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxDelay);
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return Min(until, MaxDelay);
            }
        }

        return GetBackoff(attempt);
    }

    private static TimeSpan GetBackoff(int attempt)
    {
        var exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        // Full jitter, so concurrent callers do not retry in lockstep.
        var jittered = exponential * (0.5 + (Random.Shared.NextDouble() * 0.5));
        return Min(TimeSpan.FromMilliseconds(jittered), MaxDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static async Task<byte[]?> BufferContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return null;
        }

        return await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a fresh request per attempt. An <see cref="HttpRequestMessage"/> cannot be reliably
    /// re-sent once its content has been consumed.
    /// </summary>
    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
