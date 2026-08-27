using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

// Response shapes below mirror api-specs/twilio/twilio_api_v2010 (schema
// api.v2010.account.message) and twilio_lookups_v2. Field names are the
// provider's snake_case JSON.

public class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("account_sid")] public string? AccountSid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("direction")] public string? Direction { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_updated")] public string? DateUpdated { get; set; }
    [JsonPropertyName("messaging_service_sid")] public string? MessagingServiceSid { get; set; }
    [JsonPropertyName("num_segments")] public string? NumSegments { get; set; }
    [JsonPropertyName("num_media")] public string? NumMedia { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("price_unit")] public string? PriceUnit { get; set; }
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("api_version")] public string? ApiVersion { get; set; }
}

public class TwilioListMessagesResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource> Messages { get; set; } = new();
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
}

/// <summary>Twilio's standard REST error payload.</summary>
public class TwilioErrorResource
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

public class TwilioLookupResource
{
    [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
    [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
}
