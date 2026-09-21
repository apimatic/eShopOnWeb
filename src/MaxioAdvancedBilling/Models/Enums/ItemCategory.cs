using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// One of the following: Business Software, Consumer Software, Digital Services, Physical Goods, Other
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ItemCategory>))]
public sealed record ItemCategory : StringEnum<ItemCategory>
{
    private ItemCategory(string value) : base(value)
    {
    }

    public static readonly ItemCategory BusinessSoftware = new("Business Software");

    public static readonly ItemCategory ConsumerSoftware = new("Consumer Software");

    public static readonly ItemCategory DigitalServices = new("Digital Services");

    public static readonly ItemCategory PhysicalGoods = new("Physical Goods");

    public static readonly ItemCategory Other = new("Other");

    public static ItemCategory FromValue(string value) => FromValueCore(value);
}
