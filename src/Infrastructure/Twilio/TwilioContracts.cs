using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

// These types mirror exactly the response schemas in the Twilio OpenAPI specifications
// (api-specs/twilio). Field names and casing come from the spec (snake_case), which is the
// authoritative contract for every interaction.

/// <summary>Twilio v2010 <c>api.v2010.account.message</c> resource.</summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}

/// <summary>Twilio v2010 <c>ListMessageResponse</c> envelope.</summary>
internal sealed class TwilioMessageListResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Twilio standard error payload (v2010 and Lookups).</summary>
internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
}

/// <summary>Twilio Lookups v2 <c>LookupResponse</c>.</summary>
internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}
