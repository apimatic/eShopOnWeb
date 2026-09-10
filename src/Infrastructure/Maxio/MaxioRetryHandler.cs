using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures (HTTP 429 and 5xx) with exponential backoff and
/// jitter, honoring a Retry-After header when present. Maxio limits by concurrency
/// (max 4 concurrent calls) and slows/queues abusive traffic, so this handler backs
/// off rather than hammering. Write requests are made safe to retry by the caller
/// supplying a uniqueness_token, which lets Maxio de-duplicate a replayed request.
/// </summary>
public sealed class MaxioRetryHandler : DelegatingHandler
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
        // Buffer the request body so the request can be safely re-sent on retry.
        byte[]? bodyBuffer = null;
        MediaTypeHeaderValue? contentType = null;
        if (request.Content is not null)
        {
            bodyBuffer = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType;
        }

        HttpResponseMessage? response = null;
        for (var attempt = 0; ; attempt++)
        {
            if (bodyBuffer is not null)
            {
                var content = new ByteArrayContent(bodyBuffer);
                content.Headers.ContentType = contentType;
                request.Content = content;
            }

            response = await base.SendAsync(request, cancellationToken);

            if (attempt >= MaxRetries || !IsTransient(response.StatusCode))
            {
                return response;
            }

            var delay = ComputeDelay(attempt, response);
            _logger.LogWarning(
                "Maxio request {Method} {Uri} returned {StatusCode}; retrying in {Delay}ms (attempt {Attempt}/{Max}).",
                request.Method, request.RequestUri, (int)response.StatusCode, delay.TotalMilliseconds, attempt + 1, MaxRetries);

            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < MaxDelay ? delta : MaxDelay;
        }

        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        if (exponential > MaxDelay)
        {
            exponential = MaxDelay;
        }

        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return exponential + jitter;
    }
}
