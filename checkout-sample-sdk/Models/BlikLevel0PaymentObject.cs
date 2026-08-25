using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Information used to pay using BLIK level_0 flow.
/// </summary>
public record BlikLevel0PaymentObject
{
    /// <summary>
    /// The 6-digit code used to authenticate a consumer within BLIK.
    /// </summary>
    [JsonPropertyName("auth_code")]
    [StringLength(6, MinimumLength = 6)]
    [RegularExpression("^[0-9]{6}$")]
    public required string AuthCode { get; init; }
}
