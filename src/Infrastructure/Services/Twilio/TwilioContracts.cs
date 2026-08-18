using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Wire model for <c>api.v2010.account.message</c> (Twilio Messages resource). Field names and
/// shapes come straight from the api-specs document; only the properties this integration reads
/// are mapped.
/// </summary>
internal sealed class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
    [JsonPropertyName("direction")] public string? Direction { get; set; }
}

/// <summary>Wire model for <c>ListMessageResponse</c>.</summary>
internal sealed class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource> Messages { get; set; } = new();
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

/// <summary>Wire model for the Lookups v2 <c>LookupResponse</c> (only the fields we use).</summary>
internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("valid")] public bool? Valid { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}

/// <summary>Twilio's standard error body.</summary>
internal sealed class TwilioErrorResponse
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}
