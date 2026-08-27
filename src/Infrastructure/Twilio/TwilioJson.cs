using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// JSON shapes of the Twilio contracts (snake_case), shared by the hand-written clients.
/// Property names come from the OpenAPI schemas in api-specs/twilio.
/// </summary>
internal static class TwilioJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    // Twilio renders timestamps as RFC 2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
    public static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>api.v2010.account.message</summary>
internal class TwilioMessageResource
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
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }

    public TwilioMessageInfo ToModel() => new TwilioMessageInfo
    {
        Sid = Sid ?? string.Empty,
        Status = Status,
        To = To,
        From = From,
        Body = Body,
        ErrorCode = ErrorCode,
        ErrorMessage = ErrorMessage,
        DateCreated = TwilioJson.ParseTwilioDate(DateCreated),
        DateSent = TwilioJson.ParseTwilioDate(DateSent)
    };
}

/// <summary>ListMessageResponse</summary>
internal class TwilioListMessageResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Twilio REST error model.</summary>
internal class TwilioErrorResource
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

/// <summary>Lookups v2 phone number response.</summary>
internal class TwilioLookupPhoneNumberResource
{
    [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}
