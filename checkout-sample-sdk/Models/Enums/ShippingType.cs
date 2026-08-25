using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// A classification for the method of purchase fulfillment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ShippingType>))]
public sealed record ShippingType : StringEnum<ShippingType>
{
    private ShippingType(string value) : base(value)
    {
    }

    /// <summary>
    /// The payer intends to receive the items at a specified address.
    /// </summary>
    public static readonly ShippingType Shipping = new("SHIPPING");

    /// <summary>
    /// DEPRECATED. To ensure that seller protection is correctly assigned, please use 'PICKUP_IN_STORE' or 'PICKUP_FROM_PERSON' instead. Currently, this field indicates that the payer intends to pick up the items at a specified address (ie. a store address).
    /// </summary>
    public static readonly ShippingType Pickup = new("PICKUP");

    /// <summary>
    /// The payer intends to pick up the item(s) from the payee's physical store. Also termed as BOPIS, "Buy Online, Pick-up in Store". Seller protection is provided with this option.
    /// </summary>
    public static readonly ShippingType PickupInStore = new("PICKUP_IN_STORE");

    /// <summary>
    /// The payer intends to pick up the item(s) from the payee in person. Also termed as BOPIP, "Buy Online, Pick-up in Person". Seller protection is not available, since the payer is receiving the item from the payee in person, and can validate the item prior to payment.
    /// </summary>
    public static readonly ShippingType PickupFromPerson = new("PICKUP_FROM_PERSON");

    public static ShippingType FromValue(string value) => FromValueCore(value);
}
