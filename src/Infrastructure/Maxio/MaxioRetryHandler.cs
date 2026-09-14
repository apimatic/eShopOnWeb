using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio calls that are safe to repeat, with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// Maxio limits by concurrency rather than by rate and answers 429 when a caller runs too hot,
/// so backing off (rather than reissuing immediately or in parallel) is the documented remedy.
/// <para>
/// Reads are retried on 429, on 5xx, and on transport failures. Writes are retried only on 429,
/// which is the one response that guarantees Maxio did not process the request - a 5xx or a
/// dropped connection may well have created something, and reissuing a create blindly is how
/// customers get billed twice. Recovering from those is the caller's job, and
/// <see cref="MaxioSubscriptionService"/> does it by looking for the record the failed attempt
/// may have created.
/// </para>
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        // The content of the original request can only be sent once, so buffer it up front and
        // rebuild the message for every attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType;

        for (var attempt = 1; ; attempt++)
        {
            var attemptRequest = attempt == 1 ? request : Clone(request, body, contentType);

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);

                if (!ShouldRetry(response.StatusCode, isRead) || attempt >= MaxAttempts)
                {
                    return response;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A read that never reached Maxio can be reissued safely; a write cannot.
                if (!isRead || attempt >= MaxAttempts)
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = GetDelay(attempt, response);
            _logger.LogWarning(
                "Maxio {Method} {Path} attempt {Attempt}/{MaxAttempts} failed ({Outcome}); retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                MaxAttempts,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                (int)delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRead)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return isRead && (int)statusCode >= 500;
    }

    private static TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        // Honour Retry-After when Maxio sends one; it knows better than the backoff curve does.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < MaxDelay ? delta : MaxDelay;
        }

        var backoff = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        if (backoff > MaxDelay)
        {
            backoff = MaxDelay;
        }

        // Jitter so that concurrent callers do not line up and hit the concurrency limit together.
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        return TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * jitter);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body, System.Net.Http.Headers.MediaTypeHeaderValue? contentType)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (contentType is not null)
            {
                clone.Content.Headers.ContentType = contentType;
            }
        }

        return clone;
    }
}
