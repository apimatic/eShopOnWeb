using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Price point type. We expose the following types:
/// 1. <b>default</b>: a price point that is marked as a default price for a certain product.
/// 2. <b>custom</b>: a custom price point.
/// 3. <b>catalog</b>: a price point that is <b>not</b> marked as a default price for a certain product and is <b>not</b> a custom one.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PricePointType>))]
public sealed record PricePointType : StringEnum<PricePointType>
{
    private PricePointType(string value) : base(value)
    {
    }

    public static readonly PricePointType Catalog = new("catalog");

    public static readonly PricePointType Default = new("default");

    public static readonly PricePointType Custom = new("custom");

    public static PricePointType FromValue(string value) => FromValueCore(value);
}
