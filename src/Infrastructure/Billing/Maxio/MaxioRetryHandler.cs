using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring any
/// Retry-After the server sends.
/// </summary>
/// <remarks>
/// Only requests that are safe to repeat are retried. Reads are retried on connection
/// failures and on 429/5xx. Writes are retried on 429 alone - Maxio rejects a throttled
/// request before it does any work, whereas a 5xx or a dropped connection could mean the
/// customer or subscription was in fact created, and re-sending it would duplicate.
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> RetryableServerStatuses = new()
    {
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private const HttpStatusCode TooManyRequests = (HttpStatusCode)429;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        // Buffer once so every attempt can send the same body.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;

            // A given HttpRequestMessage may only be sent once, so each attempt gets a copy.
            using var attemptRequest = Clone(request, body);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested
                                       && isRead
                                       && !isLastAttempt)
            {
                _logger.LogWarning(
                    ex,
                    "Maxio {Method} {Path} failed to complete (attempt {Attempt} of {MaxAttempts}); retrying.",
                    request.Method, request.RequestUri?.AbsolutePath, attempt, maxAttempts);

                await DelayAsync(Backoff(options, attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (isLastAttempt || !ShouldRetry(response.StatusCode, isRead))
            {
                return response;
            }

            var delay = RetryAfter(response) ?? Backoff(options, attempt);

            _logger.LogWarning(
                "Maxio {Method} {Path} returned {StatusCode} (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}.",
                request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, attempt, maxAttempts, delay);

            response.Dispose();
            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in request.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRead) =>
        statusCode == TooManyRequests || (isRead && RetryableServerStatuses.Contains(statusCode));

    private static TimeSpan Backoff(MaxioOptions options, int attempt)
    {
        var baseDelay = Math.Max(0, options.RetryBaseDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay;
        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, MaxDelay.TotalMilliseconds));
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
                    ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaxDelay ? MaxDelay : delay;
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}
