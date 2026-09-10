using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The item category type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ItemCategory>))]
public sealed record ItemCategory : StringEnum<ItemCategory>
{
    private ItemCategory(string value) : base(value)
    {
    }

    /// <summary>
    /// Goods that are stored, delivered, and used in their electronic format. This value is not currently supported for API callers that leverage the PayPal for Commerce Platform product.
    /// </summary>
    public static readonly ItemCategory DigitalGoods = new("DIGITAL_GOODS");

    /// <summary>
    /// A tangible item that can be shipped with proof of delivery.
    /// </summary>
    public static readonly ItemCategory PhysicalGoods = new("PHYSICAL_GOODS");

    /// <summary>
    /// A contribution or gift for which no good or service is exchanged, usually to a not for profit organization.
    /// </summary>
    public static readonly ItemCategory Donation = new("DONATION");

    public static ItemCategory FromValue(string value) => FromValueCore(value);
}
