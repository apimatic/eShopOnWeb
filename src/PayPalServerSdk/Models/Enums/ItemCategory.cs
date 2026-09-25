using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The item category type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ItemCategory>))]
public sealed record ItemCategory : OpenStringEnum<ItemCategory>
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

    public TResult Match<TResult>(Func<TResult> onDigitalGoods,
        Func<TResult> onPhysicalGoods,
        Func<TResult> onDonation,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == DigitalGoods => onDigitalGoods(),
            _ when this == PhysicalGoods => onPhysicalGoods(),
            _ when this == Donation => onDonation(),
            _ => otherwise(Value)
        };

    public void Match(Action onDigitalGoods, Action onPhysicalGoods, Action onDonation, Action<string> otherwise)
    {
        if (this == DigitalGoods) onDigitalGoods();
        else if (this == PhysicalGoods) onPhysicalGoods();
        else if (this == Donation) onDonation();
        else otherwise(Value);
    }
}
