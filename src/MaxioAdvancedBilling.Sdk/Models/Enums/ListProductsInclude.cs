using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListProductsInclude>))]
public sealed record ListProductsInclude : StringEnum<ListProductsInclude>
{
    private ListProductsInclude(string value) : base(value)
    {
    }

    public static readonly ListProductsInclude PrepaidProductPricePoint = new("prepaid_product_price_point");

    public static ListProductsInclude FromValue(string value) => FromValueCore(value);
}
