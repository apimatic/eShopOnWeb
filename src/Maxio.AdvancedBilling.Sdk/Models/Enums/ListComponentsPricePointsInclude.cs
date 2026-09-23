using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListComponentsPricePointsInclude>))]
public sealed record ListComponentsPricePointsInclude : StringEnum<ListComponentsPricePointsInclude>
{
    private ListComponentsPricePointsInclude(string value) : base(value)
    {
    }

    public static readonly ListComponentsPricePointsInclude CurrencyPrices = new("currency_prices");

    public static ListComponentsPricePointsInclude FromValue(string value) => FromValueCore(value);
}
