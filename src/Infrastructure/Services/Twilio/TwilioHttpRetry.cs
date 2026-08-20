using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttpRetry
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        bool isIdempotent,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;
        Exception? lastException = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            lastResponse?.Dispose();
            try
            {
                lastResponse = await client.SendAsync(requestFactory(), cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                if (!isIdempotent || attempt == MaxAttempts - 1)
                {
                    throw;
                }

                await DelayAsync(attempt, retryAfter: null, cancellationToken);
                continue;
            }

            var status = (int)lastResponse.StatusCode;
            var retryable = status == 429 || status == 503 || (isIdempotent && status == 500);
            if (!retryable || attempt == MaxAttempts - 1)
            {
                return lastResponse;
            }

            TimeSpan? retryAfter = null;
            if (lastResponse.Headers.RetryAfter?.Delta is TimeSpan delta)
            {
                retryAfter = delta;
            }

            lastResponse.Dispose();
            lastResponse = null;
            await DelayAsync(attempt, retryAfter, cancellationToken);
        }

        if (lastResponse is not null)
        {
            return lastResponse;
        }

        throw lastException ?? new HttpRequestException("Twilio request failed without a response.");
    }

    private static Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (retryAfter is TimeSpan serverDelay)
        {
            return Task.Delay(serverDelay, cancellationToken);
        }

        var windowMs = Math.Min(Cap.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = Random.Shared.NextDouble() * windowMs;
        return Task.Delay(TimeSpan.FromMilliseconds(jitter), cancellationToken);
    }
}
