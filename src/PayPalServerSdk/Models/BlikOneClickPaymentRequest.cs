using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// Information used to pay using BLIK one-click flow.
/// </summary>
public record BlikOneClickPaymentRequest
{
    /// <summary>
    /// The 6-digit code used to authenticate a consumer within BLIK.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auth_code")]
    [StringLength(6, MinimumLength = 6)]
    [RegularExpression("^[0-9]{6}$")]
    public string? AuthCode { get; init; }

    /// <summary>
    /// The merchant generated, unique reference serving as a primary identifier for accounts connected between Blik and a merchant.
    /// </summary>
    [JsonPropertyName("consumer_reference")]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[ -~]{3,64}$")]
    public required string ConsumerReference { get; init; }

    /// <summary>
    /// A bank defined identifier used as a display name to allow the payer to differentiate between multiple registered bank accounts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("alias_label")]
    [StringLength(35, MinimumLength = 8)]
    [RegularExpression("^[ -~]{8,35}$")]
    public string? AliasLabel { get; init; }

    /// <summary>
    /// A Blik-defined identifier for a specific Blik-enabled bank account that is associated with a given merchant. Used only in conjunction with a Consumer Reference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("alias_key")]
    [StringLength(19, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$")]
    public string? AliasKey { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
