using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A small resilience handler that retries transient failures with exponential backoff.
/// </summary>
/// <remarks>
/// To stay safe without an application-level idempotency key, retries are limited to
/// <b>idempotent</b> requests (GET/HEAD). Non-idempotent writes (POST) are never retried by
/// this handler, so a create is issued at most once per call; the service layer provides the
/// higher-level idempotency (lookup-before-create for customers, list-before-create for
/// subscriptions). Honours the <c>Retry-After</c> header on HTTP 429.
/// </remarks>
internal sealed class MaxioTransientErrorHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(300);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool isIdempotent = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!isIdempotent || attempt >= MaxRetries || !IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (isIdempotent && attempt < MaxRetries)
            {
                // fall through to backoff and retry
            }
            catch (TaskCanceledException) when (isIdempotent && attempt < MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                // request timeout (not caller cancellation) — retry
            }

            var delay = ComputeDelay(attempt, response);
            response?.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        if (retryAfter is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        // Exponential backoff: 300ms, 600ms, 1200ms ...
        return TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
    }
}
