using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries Maxio requests that are safe to repeat.
/// <para>
/// Reads are retried on transport faults, timeouts and transient status codes. Writes
/// (<c>POST /customers.json</c>, <c>POST /subscriptions.json</c>) are only retried on <c>429 Too
/// Many Requests</c>, which means the request was throttled before it was processed; retrying a
/// write that may already have been applied would risk a duplicate customer or subscription.
/// </para>
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Budget for a single attempt. The <see cref="HttpClient"/> timeout covers all attempts
    /// together, so without this a slow first attempt would consume the whole budget and leave
    /// nothing for a retry.
    /// </summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isReplayable = IsReplayable(request);

        // Buffer the body up front so a retry can be sent from a fresh request message; the one
        // that was already sent - and its content - must not be reused.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentHeaders = request.Content?.Headers.ToList();

        var attemptRequest = request;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= MaxAttempts;

            try
            {
                using var attemptCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(AttemptTimeout);

                var response = await base.SendAsync(attemptRequest, attemptCancellation.Token);

                if (isLastAttempt || !ShouldRetry(response.StatusCode, isReplayable))
                {
                    return response;
                }

                var delay = GetRetryAfter(response) ?? GetBackoff(attempt);
                _logger.LogWarning(
                    "Maxio {Method} {Path} answered {StatusCode}; retrying in {DelayMilliseconds}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode,
                    (int)delay.TotalMilliseconds, attempt, MaxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && isReplayable && !isLastAttempt)
            {
                var delay = GetBackoff(attempt);
                _logger.LogWarning(ex,
                    "Maxio {Method} {Path} failed; retrying in {DelayMilliseconds}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method.Method, request.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt, MaxAttempts);

                await Task.Delay(delay, cancellationToken);
            }

            if (attemptRequest != request)
            {
                attemptRequest.Dispose();
            }

            attemptRequest = Clone(request, body, contentHeaders);
        }
    }

    /// <summary>A request is replayable when repeating it cannot create a second resource.</summary>
    private static bool IsReplayable(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isReplayable)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (!isReplayable)
        {
            return false;
        }

        return statusCode == HttpStatusCode.RequestTimeout
               || statusCode == HttpStatusCode.InternalServerError
               || statusCode == HttpStatusCode.BadGateway
               || statusCode == HttpStatusCode.ServiceUnavailable
               || statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            // A cancellation the caller did not ask for is the HttpClient timeout firing.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            content.Headers.Clear();

            foreach (var header in contentHeaders ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        var delay = retryAfter.Delta
                    ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaxDelay ? MaxDelay : delay;
    }

    /// <summary>Exponential backoff with jitter, so retries from parallel callers spread out.</summary>
    private static TimeSpan GetBackoff(int attempt)
    {
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var capped = exponential > MaxDelay ? MaxDelay : exponential;
        var jitter = Random.Shared.NextDouble() * 0.25 + 0.875;

        return TimeSpan.FromMilliseconds(capped.TotalMilliseconds * jitter);
    }
}
