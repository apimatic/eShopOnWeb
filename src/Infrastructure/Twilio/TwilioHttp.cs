using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttp
{
    public static void ApplyBasicAuthentication(HttpRequestMessage request, TwilioOptions options)
    {
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credential);
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        int? code = null;
        string? providerMessage = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode))
            {
                code = parsedCode;
            }
            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                providerMessage = messageElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Error bodies are provider-owned; expose a safe generic message if they are malformed.
        }

        throw new MessagingProviderException(
            providerMessage ?? $"Twilio returned HTTP {(int)response.StatusCode}.", code);
    }

    public static DateTimeOffset? ParseDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }
}
