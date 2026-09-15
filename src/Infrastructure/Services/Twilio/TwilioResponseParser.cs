using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioResponseParser
{
    public static T Deserialize<T>(string payload, JsonSerializerOptions options)
    {
        var parsed = JsonSerializer.Deserialize<T>(payload, options);
        if (parsed is null)
        {
            throw new TwilioApiException(0, null, null);
        }

        return parsed;
    }

    public static TwilioApiException ToApiException(int httpStatus, string payload)
    {
        int? code = null;
        string? message = null;
        try
        {
            var error = JsonSerializer.Deserialize<TwilioRestError>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (error is not null)
            {
                code = error.Code == 0 ? null : error.Code;
                message = error.Message;
            }
        }
        catch (JsonException)
        {
            // Provider body is not the documented JSON error object; omit it from our exception.
        }

        return new TwilioApiException(httpStatus, code, message);
    }

    public static SmsMessage ToSmsMessage(TwilioMessageResource resource) =>
        new(
            resource.Sid ?? string.Empty,
            resource.Status ?? "unknown",
            resource.Body,
            resource.From,
            resource.To,
            resource.ErrorCode,
            resource.ErrorMessage,
            ParseRfc2822(resource.DateCreated),
            ParseRfc2822(resource.DateSent));

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
