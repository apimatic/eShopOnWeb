using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The phone number in its canonical international <see href="https://www.itu.int/rec/T-REC-E.164/en">E.164 numbering plan format</see>., The phone number, in its canonical international <see href="https://www.itu.int/rec/T-REC-E.164/en">E.164 numbering plan format</see>.
/// </summary>
public record PhoneNumber
{
    /// <summary>
    /// The national number, in its canonical international <see href="https://www.itu.int/rec/T-REC-E.164/en">E.164 numbering plan format</see>. The combined length of the country calling code (CC) and the national number must not be greater than 15 digits. The national number consists of a national destination code (NDC) and subscriber number (SN).
    /// </summary>
    [JsonPropertyName("national_number")]
    [StringLength(14, MinimumLength = 1)]
    [RegularExpression("^[0-9]{1,14}?$")]
    public required string NationalNumber { get; init; }
}
