using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    private static readonly TimeSpan RetryBase = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryCap = TimeSpan.FromSeconds(30);
    private const int MaxTries = 5;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var raw = Encoding.ASCII.GetBytes($"{accountSid}:{authToken}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        AuthenticationHeaderValue authorization,
        bool allowRetryOnServerError,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            request.Headers.Authorization = authorization;
            response = await httpClient.SendAsync(request, cancellationToken);

            if (!IsRetryable(response.StatusCode, allowRetryOnServerError) || attempt == MaxTries - 1)
            {
                return response;
            }

            var delay = DelayFor(response, attempt);
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

        var body = await response.Content.ReadAsStringAsync();
        var providerCode = TryReadProviderCode(body);
        var suffix = providerCode.HasValue ? $" (provider code {providerCode.Value})" : string.Empty;
        throw new HttpRequestException($"{operation} failed with HTTP {(int)response.StatusCode}{suffix}.");
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var parsed = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        if (parsed is null)
        {
            throw new HttpRequestException("The provider returned an empty JSON body.");
        }

        return parsed;
    }

    private static bool IsRetryable(HttpStatusCode statusCode, bool allowRetryOnServerError)
    {
        if (statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return allowRetryOnServerError && (int)statusCode >= 500;
    }

    private static TimeSpan DelayFor(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var windowMs = Math.Min(RetryCap.TotalMilliseconds, RetryBase.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = Random.Shared.NextDouble() * windowMs;
        return TimeSpan.FromMilliseconds(jitter);
    }

    private static int? TryReadProviderCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Body is not JSON; omit the provider code rather than surfacing payload text.
        }

        return null;
    }
}

internal sealed class TwilioErrorBody
{
    public int? Code { get; set; }
    public string? Message { get; set; }
    public int? Status { get; set; }
}

internal sealed class TwilioLookupResponse
{
    public bool Valid { get; set; }
    public string? PhoneNumber { get; set; }
    public List<string>? ValidationErrors { get; set; }
}

internal sealed class TwilioMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
}

internal sealed class TwilioMessageListDto
{
    public List<TwilioMessageDto>? Messages { get; set; }
    public string? NextPageUri { get; set; }
}
