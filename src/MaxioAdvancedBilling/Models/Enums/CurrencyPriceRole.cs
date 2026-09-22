using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Role for the price.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CurrencyPriceRole>))]
public sealed record CurrencyPriceRole : StringEnum<CurrencyPriceRole>
{
    private CurrencyPriceRole(string value) : base(value)
    {
    }

    public static readonly CurrencyPriceRole Baseline = new("baseline");

    public static readonly CurrencyPriceRole Trial = new("trial");

    public static readonly CurrencyPriceRole Initial = new("initial");

    public static CurrencyPriceRole FromValue(string value) => FromValueCore(value);
}
