using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PricingScheme>))]
public sealed record PricingScheme : StringEnum<PricingScheme>
{
    private PricingScheme(string value) : base(value)
    {
    }

    public static readonly PricingScheme Stairstep = new("stairstep");

    public static readonly PricingScheme Volume = new("volume");

    public static readonly PricingScheme PerUnit = new("per_unit");

    public static readonly PricingScheme Tiered = new("tiered");

    public static PricingScheme FromValue(string value) => FromValueCore(value);
}
