using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written DTOs matching the Twilio OpenAPI schemas
/// (api.v2010.account.message, ListMessageResponse, LookupResponse, error model).
/// </summary>
internal sealed class TwilioMessageDto
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("account_sid")] public string? AccountSid { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("direction")] public string? Direction { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_updated")] public string? DateUpdated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
    [JsonPropertyName("num_segments")] public string? NumSegments { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("price_unit")] public string? PriceUnit { get; set; }
    [JsonPropertyName("uri")] public string? Uri { get; set; }

    public ProviderMessage ToProviderMessage() => new(
        Sid ?? string.Empty,
        From,
        To,
        Status ?? string.Empty,
        ErrorCode,
        ErrorMessage,
        Body,
        ParseRfc2822(DateCreated),
        ParseRfc2822(DateSent),
        ParseRfc2822(DateUpdated));

    // The spec types these as date-time-rfc-2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
    internal static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}

internal sealed class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageDto>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
}

internal sealed class TwilioErrorDto
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

internal sealed class TwilioLookupResponseDto
{
    [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}
