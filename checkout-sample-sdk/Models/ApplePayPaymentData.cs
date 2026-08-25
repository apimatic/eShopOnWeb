using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Information about the decrypted apple pay payment data for the token like cryptogram, eci indicator.
/// </summary>
public record ApplePayPaymentData
{
    /// <summary>
    /// Online payment cryptogram, as defined by 3D Secure. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cryptogram")]
    [StringLength(2000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Cryptogram { get; init; }

    /// <summary>
    /// ECI indicator, as defined by 3- Secure. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("eci_indicator")]
    [StringLength(256, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? EciIndicator { get; init; }

    /// <summary>
    /// Encoded Apple Pay EMV Payment Structure used for payments in China. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emv_data")]
    [StringLength(2000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? EmvData { get; init; }

    /// <summary>
    /// Bank Key encrypted Apple Pay PIN. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pin")]
    [StringLength(2000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Pin { get; init; }
}
