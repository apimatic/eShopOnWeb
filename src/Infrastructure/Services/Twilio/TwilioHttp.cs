using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        bool retryServerErrors,
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
            response = await httpClient.SendAsync(request, cancellationToken);

            if (!IsRetryable(response.StatusCode, retryServerErrors) || attempt == maxTries - 1)
            {
                return response;
            }

            var delay = ReadRetryAfter(response) ?? FullJitter(baseDelay, cap, attempt);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, params HttpStatusCode[] extraAllowed)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        foreach (var allowed in extraAllowed)
        {
            if (response.StatusCode == allowed)
            {
                return;
            }
        }

        int? errorCode = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(payload))
            {
                var error = JsonSerializer.Deserialize<TwilioErrorBody>(payload, JsonOptions);
                errorCode = error?.Code;
            }
        }
        catch (JsonException)
        {
            // Body is not the documented error object; surface HTTP status only.
        }

        throw new TwilioApiException((int)response.StatusCode, errorCode);
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        if (value is null)
        {
            throw new TwilioApiException((int)response.StatusCode, errorCode: null);
        }

        return value;
    }

    private static bool IsRetryable(HttpStatusCode statusCode, bool retryServerErrors)
    {
        if (statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return retryServerErrors && statusCode == HttpStatusCode.InternalServerError;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return null;
    }

    private static TimeSpan FullJitter(TimeSpan baseDelay, TimeSpan cap, int attempt)
    {
        var windowMs = Math.Min(cap.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)windowMs + 1));
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }
    }
}
