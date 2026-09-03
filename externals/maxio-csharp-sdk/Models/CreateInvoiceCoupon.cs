using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateInvoiceCoupon
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subcode")]
    public string? Subcode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("percentage")]
    public Percentage1? Percentage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Amount2? Amount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [MaxLength(255)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_family_id")]
    public ProductFamilyId? ProductFamilyId { get; init; }

    /// <summary>
    /// Applicable only to stackable coupons. For <c>compound</c>, Percentage-based discounts will be calculated against the remaining price, after prior discounts have been calculated. For <c>full-price</c>, Percentage-based discounts will always be calculated against the original item price, before other discounts are applied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("compounding_strategy")]
    public CompoundingStrategy? CompoundingStrategy { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
