using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

// JSON models matching the schemas of the Twilio OpenAPI documents in api-specs/
// (twilio_api_v2010: api.v2010.account.message, ListMessageResponse;
//  twilio_lookups_v2: lookups.v2.phone_number).

internal class TwilioMessageResource
{
    [JsonPropertyName("sid")] public string? Sid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
}

internal class TwilioListMessageResponse
{
    [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
}

internal class TwilioErrorResource
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
}

internal class TwilioLookupPhoneNumberResource
{
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}
