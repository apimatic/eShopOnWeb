using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff. Maxio uses concurrency-based rate
/// limiting and asks clients to back off (not parallelize) on 429; this handler honors that and
/// also retries 5xx and transient network errors. Non-idempotent POSTs are protected against
/// duplicates by a uniqueness_token, so retrying them is safe.
/// </summary>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!IsTransient(response.StatusCode) || attempt >= MaxRetries)
                    return response;
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                // fall through to backoff and retry
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                // request timeout (not caller cancellation) -> retry
            }

            var delay = ComputeDelay(response, attempt);
            _logger.LogWarning(
                "Maxio request {Method} {Uri} attempt {Attempt} was transient ({Status}); retrying in {Delay}ms.",
                request.Method, request.RequestUri, attempt + 1,
                response?.StatusCode.ToString() ?? "network error", delay.TotalMilliseconds);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan ComputeDelay(HttpResponseMessage? response, int attempt)
    {
        // Respect a server-provided Retry-After when present.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
                return delta;
            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    return wait;
            }
        }

        // Exponential backoff: 0.5s, 1s, 2s ...
        return TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
    }
}
