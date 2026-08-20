using System;
using System.Net.Http;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? providerCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }

    internal static TwilioApiException FromResponse(int statusCode, string? body)
    {
        int? providerCode = null;
        var message = "Twilio request failed.";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String)
                {
                    message = PhoneNumberRedactor.Redact(messageEl.GetString());
                }
                if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code))
                {
                    providerCode = code;
                }
            }
            catch (JsonException)
            {
                message = PhoneNumberRedactor.Redact(body);
            }
        }

        return new TwilioApiException(statusCode, providerCode, message);
    }
}
