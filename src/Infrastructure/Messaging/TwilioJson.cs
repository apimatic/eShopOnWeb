using System;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
               ?? throw new InvalidOperationException("The provider returned an empty response.");
    }

    public static void ThrowIfFailed(HttpResponseMessage response, string body, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = TryReadErrorCode(body);
        throw new SmsProviderException(operation, (int)response.StatusCode, code);
    }

    private static int? TryReadErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

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
            // Provider error bodies are not logged: they can include destination numbers.
        }

        return null;
    }
}
