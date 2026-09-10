using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The tax ID of the customer. The customer is also known as the payer. Both <c>tax_id</c> and <c>tax_id_type</c> are required.
/// </summary>
public record TaxInfo
{
    /// <summary>
    /// The customer's tax ID value.
    /// </summary>
    [JsonPropertyName("tax_id")]
    [StringLength(14, MinimumLength = 1)]
    [RegularExpression("([a-zA-Z0-9])")]
    public required string TaxId { get; init; }

    /// <summary>
    /// The customer's tax ID type.
    /// </summary>
    [JsonPropertyName("tax_id_type")]
    public required TaxIdType TaxIdType { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
