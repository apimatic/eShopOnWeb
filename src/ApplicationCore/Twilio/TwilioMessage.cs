using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Twilio;

/// <summary>
/// Mirrors the <c>api.v2010.account.message</c> schema from the Twilio
/// api_v2010 OpenAPI document (snake_case JSON property names).
/// </summary>
public class TwilioMessage
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("account_sid")]
    public string? AccountSid { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; set; }

    [JsonPropertyName("num_segments")]
    public string? NumSegments { get; set; }

    [JsonPropertyName("num_media")]
    public string? NumMedia { get; set; }

    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreatedRaw { get; set; }

    [JsonPropertyName("date_updated")]
    public string? DateUpdatedRaw { get; set; }

    [JsonPropertyName("date_sent")]
    public string? DateSentRaw { get; set; }

    // The spec types these as date-time-rfc-2822.
    public DateTimeOffset? DateCreated => ParseRfc2822(DateCreatedRaw);
    public DateTimeOffset? DateUpdated => ParseRfc2822(DateUpdatedRaw);
    public DateTimeOffset? DateSent => ParseRfc2822(DateSentRaw);

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// Mirrors the <c>ListMessageResponse</c> schema from the Twilio api_v2010
/// OpenAPI document.
/// </summary>
public class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessage>? Messages { get; set; }

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }

    [JsonPropertyName("previous_page_uri")]
    public string? PreviousPageUri { get; set; }

    [JsonPropertyName("first_page_uri")]
    public string? FirstPageUri { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }
}
