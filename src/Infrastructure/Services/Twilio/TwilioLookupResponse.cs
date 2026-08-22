using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// LookupResponse from twilio_lookups_v2.yaml (GET /v2/PhoneNumbers/{PhoneNumber}).
/// </summary>
internal sealed class TwilioLookupResponse
{
    [JsonPropertyName("calling_country_code")]
    public string? CallingCountryCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}
