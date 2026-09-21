using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Applicable only to stackable coupons. For <c>compound</c>, Percentage-based discounts will be calculated against the remaining price, after prior discounts have been calculated. For <c>full-price</c>, Percentage-based discounts will always be calculated against the original item price, before other discounts are applied.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CompoundingStrategy>))]
public sealed record CompoundingStrategy : StringEnum<CompoundingStrategy>
{
    private CompoundingStrategy(string value) : base(value)
    {
    }

    public static readonly CompoundingStrategy Compound = new("compound");

    public static readonly CompoundingStrategy FullPrice = new("full-price");

    public static CompoundingStrategy FromValue(string value) => FromValueCore(value);
}
