using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

// Wire contracts hand-written to match the Twilio OpenAPI specifications in api-specs/twilio.
// Message shapes come from twilio_api_v2010 (schema api.v2010.account.message); the lookup shape
// comes from twilio_lookups_v2 (schema LookupResponse). Only the fields this integration uses are
// modelled; unknown fields are ignored on deserialization.

/// <summary>api.v2010.account.message</summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    // RFC-2822 timestamp, e.g. "Fri, 24 May 2019 17:18:28 +0000". Null until the message is sent.
    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }

    [JsonPropertyName("messaging_service_sid")]
    public string? MessagingServiceSid { get; set; }
}

/// <summary>ListMessageResponse</summary>
internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<TwilioMessageResource> Messages { get; set; } = new();

    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}

/// <summary>LookupResponse (twilio_lookups_v2)</summary>
internal sealed class TwilioLookupResource
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}

/// <summary>Twilio error envelope: { code, message, more_info, status }.</summary>
internal sealed class TwilioErrorResource
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
