using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record PhoneNumber1
{
    /// <summary>
    /// Phone number to be ported. This must be in the E164 Format.
    /// </summary>
    [JsonPropertyName("phone_number")]
    public required string PhoneNumber { get; init; }

    /// <summary>
    /// Some losing carriers require a PIN to authorize the port of a phone number. If the phone number is a US mobile phone number, the PIN is mandatory to process a porting request. Other carriers and number types may also require a PIN, you'll need to contact the losing carrier to determine what your phone number's PIN is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pin")]
    public string? Pin { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
