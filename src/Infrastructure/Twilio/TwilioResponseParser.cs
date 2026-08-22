using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SmsMessageResult ToMessageResult(TwilioMessageResource? resource)
    {
        resource ??= new TwilioMessageResource();
        return new SmsMessageResult(
            resource.Sid,
            resource.Status,
            resource.ErrorCode,
            TwilioApiException.RedactPhoneNumbers(resource.ErrorMessage),
            resource.Body,
            ParseRfc2822(resource.DateCreated),
            ParseRfc2822(resource.DateSent),
            resource.Uri);
    }

    public static TwilioApiException ToException(HttpResponseMessage response, string payload)
    {
        TwilioErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // The spec does not document a structured body for every error status; use the raw payload.
        }

        var message = error?.Message ?? $"Twilio request failed with status {(int)response.StatusCode}.";
        return new TwilioApiException((int)response.StatusCode, error?.Code, message);
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
