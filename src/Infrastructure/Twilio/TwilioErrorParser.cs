using System;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioErrorParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TwilioApiException ToException(HttpStatusCode statusCode, string payload, string fallback)
    {
        int? errorCode = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<TwilioErrorBody>(payload, JsonOptions);
            errorCode = parsed?.Code;
        }
        catch (JsonException)
        {
            // The body is not used in the exception message: it can contain destination numbers.
        }

        var status = (int)statusCode;
        var message = errorCode is null
            ? $"{fallback} HTTP {status}."
            : $"{fallback} HTTP {status}, provider code {errorCode}.";
        return new TwilioApiException(status, errorCode, message);
    }

    private sealed class TwilioErrorBody
    {
        public int? Code { get; set; }
        public int? Status { get; set; }
    }
}
