using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    internal static AuthenticationHeaderValue CreateBasicAuth(TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    internal static string MessagingBaseUrl(TwilioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        return "https://api.twilio.com";
    }

    internal static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    internal static async System.Threading.Tasks.Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        string? providerMessage = null;
        int? providerCode = null;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            if (doc.RootElement.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                providerMessage = messageProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("code", out var codeProp) && codeProp.TryGetInt32(out var code))
            {
                providerCode = code;
            }
        }
        catch (JsonException)
        {
            providerMessage = raw;
        }

        var safe = PiiRedactor.Redact(providerMessage ?? response.ReasonPhrase ?? "Twilio request failed");
        throw new TwilioRestException((int)response.StatusCode, providerCode, safe);
    }
}

public sealed class TwilioRestException : Exception
{
    public TwilioRestException(int statusCode, int? providerCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
