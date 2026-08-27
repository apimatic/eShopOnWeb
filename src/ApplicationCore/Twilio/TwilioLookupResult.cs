using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Twilio;

/// <summary>
/// Mirrors the phone-number lookup response schema from the Twilio
/// lookups_v2 OpenAPI document (GET /v2/PhoneNumbers/{PhoneNumber}).
/// </summary>
public class TwilioLookupResult
{
    [JsonPropertyName("calling_country_code")]
    public string? CallingCountryCode { get; set; }

    /// <summary>The provider's canonical E.164 form of the looked-up number.</summary>
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}
