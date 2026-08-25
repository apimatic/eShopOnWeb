using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The phone information.
/// </summary>
public record PhoneWithType
{
    /// <summary>
    /// The phone type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_type")]
    public PhoneType? PhoneType { get; init; }

    /// <summary>
    /// The phone number in its canonical international <see href="https://www.itu.int/rec/T-REC-E.164/en">E.164 numbering plan format</see>.
    /// </summary>
    [JsonPropertyName("phone_number")]
    public required PhoneNumber PhoneNumber { get; init; }
}
