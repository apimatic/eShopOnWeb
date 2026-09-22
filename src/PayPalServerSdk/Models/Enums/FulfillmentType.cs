using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// A classification for the method of purchase fulfillment (e.g shipping, in-store pickup, etc). Either <c>type</c> or <c>options</c> may be present, but not both.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FulfillmentType>))]
public sealed record FulfillmentType : StringEnum<FulfillmentType>
{
    private FulfillmentType(string value) : base(value)
    {
    }

    /// <summary>
    /// The payer intends to receive the items at a specified address.
    /// </summary>
    public static readonly FulfillmentType Shipping = new("SHIPPING");

    /// <summary>
    /// DEPRECATED. Please use "PICKUP_FROM_PERSON" instead.
    /// </summary>
    public static readonly FulfillmentType PickupInPerson = new("PICKUP_IN_PERSON");

    /// <summary>
    /// The payer intends to pick up the item(s) from the payee's physical store. Also termed as BOPIS, "Buy Online, Pick-up in Store". Seller protection is provided with this option.
    /// </summary>
    public static readonly FulfillmentType PickupInStore = new("PICKUP_IN_STORE");

    /// <summary>
    /// The payer intends to pick up the item(s) from the payee in person. Also termed as BOPIP, "Buy Online, Pick-up in Person". Seller protection is not available, since the payer is receiving the item from the payee in person, and can validate the item prior to payment.
    /// </summary>
    public static readonly FulfillmentType PickupFromPerson = new("PICKUP_FROM_PERSON");

    public static FulfillmentType FromValue(string value) => FromValueCore(value);
}
