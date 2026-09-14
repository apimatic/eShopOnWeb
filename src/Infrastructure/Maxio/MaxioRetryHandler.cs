using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A <see cref="DelegatingHandler"/> that retries transient Maxio failures.
/// Maxio uses concurrency-based rate limiting and returns HTTP 429 when a
/// caller exceeds it; the guidance is to back off (never to parallelize harder).
/// This handler retries on 429 and transient 5xx / network faults with a bounded
/// exponential backoff, honoring the <c>Retry-After</c> header when present.
/// Requests are re-issued as clones so buffered bodies can be replayed safely.
/// </summary>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the request content once so the request can be replayed on retry.
        var buffered = await BufferContentAsync(request.Content, cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            var isLastAttempt = attempt >= MaxRetries;

            using var attemptRequest = await CloneRequestAsync(request, buffered).ConfigureAwait(false);

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!isLastAttempt && IsTransientException(ex, cancellationToken))
            {
                var delay = ComputeBackoff(attempt);
                _logger.LogWarning(ex, "Transient error calling Maxio ({Method} {Uri}); retry {Attempt}/{Max} in {Delay}ms.",
                    request.Method, request.RequestUri, attempt + 1, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (isLastAttempt || !IsTransientStatus(response.StatusCode))
            {
                return response;
            }

            var retryDelay = GetRetryAfter(response) ?? ComputeBackoff(attempt);
            _logger.LogWarning("Maxio returned {StatusCode} for {Method} {Uri}; retry {Attempt}/{Max} in {Delay}ms.",
                (int)response.StatusCode, request.Method, request.RequestUri, attempt + 1, MaxRetries, retryDelay.TotalMilliseconds);
            response.Dispose();

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.InternalServerError
        || statusCode == HttpStatusCode.BadGateway
        || statusCode == HttpStatusCode.ServiceUnavailable
        || statusCode == HttpStatusCode.GatewayTimeout;

    private static bool IsTransientException(Exception ex, CancellationToken cancellationToken)
    {
        // A caller-requested cancellation is not transient.
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // Network failures and per-request timeouts (TaskCanceledException raised
        // by HttpClient.Timeout) are worth retrying.
        return ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException;
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        // Exponential: 0.5s, 1s, 2s, ... capped.
        var ticks = BaseDelay.Ticks * (long)Math.Pow(2, attempt);
        var delay = TimeSpan.FromTicks(ticks);
        return delay > MaxDelay ? MaxDelay : delay;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > MaxDelay ? MaxDelay : delta;
        }

        if (retryAfter.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay > MaxDelay ? MaxDelay : delay;
            }
        }

        return null;
    }

    private static async Task<byte[]?> BufferContentAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, byte[]? bufferedContent)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        if (bufferedContent is not null)
        {
            var newContent = new ByteArrayContent(bufferedContent);
            if (request.Content?.Headers is { } originalHeaders)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in originalHeaders)
                {
                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = newContent;
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy per-request options so nothing set upstream is lost on retry.
        foreach (KeyValuePair<string, object?> option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return clone;
    }
}
