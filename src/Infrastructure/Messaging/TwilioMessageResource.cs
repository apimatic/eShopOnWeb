using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The provider's Message resource, per the api.v2010.account.message schema in
/// api-specs/twilio/twilio_api_v2010. Dates are RFC 2822 strings.
/// </summary>
internal class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }

    public ProviderMessage ToProviderMessage() => new(
        Sid ?? string.Empty,
        From,
        To,
        Body,
        Status ?? string.Empty,
        ErrorCode,
        ErrorMessage,
        ParseRfc2822(DateSent),
        ParseRfc2822(DateCreated));

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}

/// <summary>List response envelope per the ListMessageResponse schema (next_page_uri drives pagination).</summary>
internal class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public TwilioMessageResource[]? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Provider error model returned with non-2xx responses.</summary>
internal class TwilioErrorResource
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}
