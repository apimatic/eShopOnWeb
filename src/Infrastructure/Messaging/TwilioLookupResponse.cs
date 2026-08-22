using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

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

    [JsonPropertyName("line_type_intelligence")]
    public TwilioLineTypeIntelligence? LineTypeIntelligence { get; set; }
}

internal sealed class TwilioLineTypeIntelligence
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }
}
