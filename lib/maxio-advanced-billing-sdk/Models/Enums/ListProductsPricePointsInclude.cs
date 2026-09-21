using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListProductsPricePointsInclude>))]
public sealed record ListProductsPricePointsInclude : StringEnum<ListProductsPricePointsInclude>
{
    private ListProductsPricePointsInclude(string value) : base(value)
    {
    }

    public static readonly ListProductsPricePointsInclude CurrencyPrices = new("currency_prices");

    public static ListProductsPricePointsInclude FromValue(string value) => FromValueCore(value);
}
