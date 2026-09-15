using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        bool retryServerErrors,
        ILogger logger,
        string operation,
        CancellationToken cancellationToken)
    {
        const int maxTries = 5;
        var baseDelay = TimeSpan.FromMilliseconds(500);
        var cap = TimeSpan.FromSeconds(30);
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt < maxTries; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Twilio {Operation} timed out.", operation);
                throw new TwilioClientException($"Twilio {operation} timed out.");
            }

            if (!ShouldRetry(response.StatusCode, retryServerErrors) || attempt == maxTries - 1)
            {
                return response;
            }

            var delay = ResolveDelay(response, baseDelay, cap, attempt);
            logger.LogWarning(
                "Twilio {Operation} returned {StatusCode}; retrying in {DelayMs} ms.",
                operation,
                (int)response.StatusCode,
                (int)delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync();
        var error = TryDeserialize<TwilioErrorResponse>(payload);
        var message = LogRedaction.RedactPhoneNumbers(error?.Message ?? $"Twilio {operation} failed.");
        throw new TwilioClientException(message, (int)response.StatusCode, error?.Code);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new TwilioClientException("Twilio returned an empty response.");
    }

    public static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool retryServerErrors)
    {
        if (statusCode == (HttpStatusCode)429 || statusCode == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return retryServerErrors && statusCode == HttpStatusCode.InternalServerError;
    }

    private static TimeSpan ResolveDelay(HttpResponseMessage response, TimeSpan baseDelay, TimeSpan cap, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var until = retryAt - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var windowMs = Math.Min(cap.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * windowMs);
    }

    private sealed class TwilioErrorResponse
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public int? Status { get; set; }
    }
}
