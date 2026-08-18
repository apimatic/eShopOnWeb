using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The Lookups v2 response (<c>LookupResponse</c> in the spec). Only the fields needed to decide
/// "usable destination" and to obtain the canonical number are mapped.
/// </summary>
internal sealed class TwilioLookupResponse
{
    /// <summary>True when the number is in a valid, carrier-assignable range.</summary>
    [JsonPropertyName("valid")] public bool Valid { get; set; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }

    /// <summary>Reasons the number is invalid (TOO_SHORT, INVALID_COUNTRY_CODE, ...).</summary>
    [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
}
