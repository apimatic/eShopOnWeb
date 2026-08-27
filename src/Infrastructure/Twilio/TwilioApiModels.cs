using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written models matching the Twilio OpenAPI specs in api-specs/:
/// api.v2010.account.message (twilio_api_v2010) and the Lookups v2 phone number
/// resource (twilio_lookups_v2).
/// </summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }

    // The spec types dates as date-time-rfc-2822 (e.g. "Thu, 24 Aug 2023 05:01:45 +0000").
    public static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}

internal sealed class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource> Messages { get; set; } = new();
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Twilio's standard error payload (code/message/more_info/status).</summary>
internal sealed class TwilioErrorResource
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

internal sealed class TwilioPhoneNumberResource
{
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}
