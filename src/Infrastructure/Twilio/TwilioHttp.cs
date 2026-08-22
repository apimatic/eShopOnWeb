using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Extensions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", raw);
    }

    internal static string Sanitize(string? text) => PhoneNumberSanitizer.Sanitize(text);

    internal static string FormatError(int statusCode, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Twilio request failed with HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : null;
            var message = root.TryGetProperty("message", out var messageEl) ? messageEl.GetString() : body;
            return Sanitize($"Twilio HTTP {statusCode} ({code}): {message}");
        }
        catch (JsonException)
        {
            return Sanitize($"Twilio request failed with HTTP {statusCode}.");
        }
    }
}

internal sealed class TwilioApiException : Exception
{
    public TwilioApiException(string message) : base(message)
    {
    }
}
