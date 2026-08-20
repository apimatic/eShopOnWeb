using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttpRetry
{
    private const int MaxTries = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    public static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        bool allowRetryOnSuccessPath,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            response?.Dispose();
            response = await send();
            var status = (int)response.StatusCode;
            var retryable = status == 429 || status == 503 || (allowRetryOnSuccessPath && status == 500);
            if (!retryable || attempt == MaxTries - 1)
            {
                return response;
            }

            await DelayAsync(response, attempt, cancellationToken);
        }

        return response!;
    }

    public static TwilioApiException ToApiException(HttpResponseMessage response, string payload)
    {
        var status = (int)response.StatusCode;
        int? code = null;
        var message = $"Twilio request failed with HTTP {status}.";
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload);
            if (error != null)
            {
                code = error.Code == 0 ? null : error.Code;
                if (!string.IsNullOrWhiteSpace(error.Message))
                {
                    message = LogSanitizer.RedactPhoneNumbers(error.Message);
                }
            }
        }
        catch (JsonException)
        {
            // Body is not the standard four-field error object; keep the generic message.
        }

        return new TwilioApiException(status, code, message);
    }

    private static async Task DelayAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            await Task.Delay(retryAfter, cancellationToken);
            return;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return;
        }

        var windowMs = Math.Min(Cap.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * windowMs);
        await Task.Delay(jitter, cancellationToken);
    }
}
