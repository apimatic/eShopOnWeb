using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries throttled and transient Maxio failures with jittered exponential back-off.
/// <para>
/// Maxio limits by concurrency rather than call rate, so a 429 means the request was refused without
/// being processed and is always safe to repeat - slower, never wider. Everything else is only
/// repeated when the caller marked the request safe to retry (a read, or a write carrying a
/// uniqueness token), so a write is never silently duplicated.
/// </para>
/// </summary>
public class MaxioResilienceHandler : DelegatingHandler
{
    internal static readonly HttpRequestOptionsKey<bool> SafeToRetryOption = new("maxio.safe-to-retry");

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8);

    private readonly ILogger<MaxioResilienceHandler> _logger;
    private readonly int _maxRetries;

    public MaxioResilienceHandler(ILogger<MaxioResilienceHandler> logger, int maxRetries)
    {
        _logger = logger;
        _maxRetries = Math.Max(0, maxRetries);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var safeToRetry = request.Options.TryGetValue(SafeToRetryOption, out var flag)
            ? flag
            : request.Method == HttpMethod.Get;

        // Buffer the body once so the request can be rebuilt for each attempt.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        for (var attempt = 0; ; attempt++)
        {
            var isLastAttempt = attempt >= _maxRetries;
            var attemptRequest = attempt == 0 ? request : await CloneAsync(request);

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);

                var throttled = response.StatusCode == HttpStatusCode.TooManyRequests;
                var transient = response.StatusCode >= HttpStatusCode.InternalServerError
                                || response.StatusCode == HttpStatusCode.RequestTimeout;

                if (isLastAttempt || !(throttled || (transient && safeToRetry)))
                {
                    return response;
                }

                var delay = DelayFor(attempt, response);
                _logger.LogWarning(
                    "Maxio responded {StatusCode} to {Method} {Path}; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                    (int)response.StatusCode, attemptRequest.Method, attemptRequest.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt + 1, _maxRetries + 1);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientTransportFailure(ex, cancellationToken))
            {
                response?.Dispose();

                if (isLastAttempt || !safeToRetry)
                {
                    throw;
                }

                var delay = DelayFor(attempt, response: null);
                _logger.LogWarning(ex,
                    "Maxio call {Method} {Path} failed in transport; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                    attemptRequest.Method, attemptRequest.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt + 1, _maxRetries + 1);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or TaskCanceledException or TimeoutException;

    private static TimeSpan DelayFor(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            var hinted = retryAfter.Delta
                         ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);

            if (hinted is { } wait && wait > TimeSpan.Zero)
            {
                return wait > MaxDelay ? MaxDelay : wait;
            }
        }

        var backoff = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var total = backoff + jitter;
        return total > MaxDelay ? MaxDelay : total;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsByteArrayAsync();
            var content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
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
}
