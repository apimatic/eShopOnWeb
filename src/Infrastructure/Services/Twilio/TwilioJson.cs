using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static DateTimeOffset? ParseRfc2822(string? value)
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

    public static T Read<T>(string json)
    {
        var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Twilio returned an empty response body.");
        }

        return result;
    }
}

internal sealed class TwilioApiErrorBody
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }
}

internal sealed class TwilioLookupResponseBody
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

internal sealed class TwilioMessageResourceBody
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }
}

internal sealed class TwilioMessageListBody
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResourceBody> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

internal static class TwilioResponseGuard
{
    public static void ThrowIfFailed(int statusCode, string content)
    {
        if (statusCode is >= 200 and <= 299)
        {
            return;
        }

        TwilioApiErrorBody? error = null;
        try
        {
            error = JsonSerializer.Deserialize<TwilioApiErrorBody>(content, TwilioJson.SerializerOptions);
        }
        catch (JsonException)
        {
            // Fall through to a generic failure.
        }

        var sanitized = PhoneNumberSanitizer.Sanitize(error?.Message) ?? "Twilio request failed.";
        throw new TwilioApiException(statusCode, error?.Code, sanitized);
    }

    public static void ThrowIfLookupFailed(int statusCode, string content)
    {
        if (statusCode == 404)
        {
            throw new InvalidContactNumberException();
        }

        ThrowIfFailed(statusCode, content);
    }
}
