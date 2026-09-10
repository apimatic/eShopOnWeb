using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The Universal Product Code of the item.
/// </summary>
public record UniversalProductCode
{
    /// <summary>
    /// The Universal Product Code type.
    /// </summary>
    [JsonPropertyName("type")]
    public required UpcType Type { get; init; }

    /// <summary>
    /// The UPC product code of the item.
    /// </summary>
    [JsonPropertyName("code")]
    [StringLength(17, MinimumLength = 6)]
    [RegularExpression("^[0-9]{0,17}$")]
    public required string Code { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
