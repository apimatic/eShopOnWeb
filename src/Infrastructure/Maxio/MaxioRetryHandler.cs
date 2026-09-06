using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio calls that failed in a way a retry can fix, with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// Maxio limits concurrency rather than request rate, and explicitly asks callers not to answer a
/// throttled response with more parallelism. So this backs off rather than fanning out, and it
/// only replays requests that are safe to replay: reads always, and writes only when the response
/// proves the request was rejected outright (429) rather than possibly acted on. A write whose
/// response was lost is recovered by the uniqueness token instead, one layer up.
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private readonly int _maxAttempts;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(int maxRetryAttempts, ILogger<MaxioRetryHandler> logger)
    {
        _maxAttempts = Math.Max(1, maxRetryAttempts + 1);
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        // Buffer the body once so every attempt can send an identical, independent copy.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentHeaders = request.Content?.Headers;

        for (var attempt = 1; ; attempt++)
        {
            using var attemptRequest = CloneRequest(request, body, contentHeaders);

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);

                if (!ShouldRetry(response.StatusCode, isRead) || attempt >= _maxAttempts) return response;
            }
            catch (Exception ex) when (IsTransient(ex) && isRead && attempt < _maxAttempts)
            {
                transportFailure = ex;
            }

            var delay = GetDelay(attempt, response);
            _logger.LogWarning(
                "Maxio {Method} {Path} failed ({Outcome}) on attempt {Attempt} of {MaxAttempts}; retrying in {Delay}.",
                request.Method, request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                attempt, _maxAttempts, delay);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source,
        byte[]? body,
        HttpContentHeaders? contentHeaders)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is null) return clone;

        clone.Content = new ByteArrayContent(body);
        if (contentHeaders is not null)
        {
            foreach (var header in contentHeaders)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static bool ShouldRetry(HttpStatusCode status, bool isRead) => status switch
    {
        // Throttled: the request was denied, never processed, so replaying it is always safe.
        HttpStatusCode.TooManyRequests => true,
        // Server-side faults may have been acted on, so only reads get replayed.
        HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError => isRead,
        _ => false
    };

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException ||
        // HttpClient surfaces its own timeout as a cancellation with no user token attached.
        (exception is TaskCanceledException canceled && canceled.InnerException is TimeoutException);

    private static TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;

        // 1s, 2s, 4s ... plus jitter, so a burst of retries does not re-converge on the API.
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        return backoff + jitter;
    }
}
