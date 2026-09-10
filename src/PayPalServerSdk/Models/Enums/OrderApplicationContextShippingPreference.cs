using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// DEPRECATED. DEPRECATED. The shipping preference: Displays the shipping address to the customer. Enables the customer to choose an address on the PayPal site. Restricts the customer from changing the address during the payment-approval process. .  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.shipping_preference</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OrderApplicationContextShippingPreference>))]
public sealed record OrderApplicationContextShippingPreference : StringEnum<OrderApplicationContextShippingPreference>
{
    private OrderApplicationContextShippingPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// Use the customer-provided shipping address on the PayPal site.
    /// </summary>
    public static readonly OrderApplicationContextShippingPreference GetFromFile = new("GET_FROM_FILE");

    /// <summary>
    /// Redact the shipping address from the PayPal site. Recommended for digital goods.
    /// </summary>
    public static readonly OrderApplicationContextShippingPreference NoShipping = new("NO_SHIPPING");

    /// <summary>
    /// Use the merchant-provided address. The customer cannot change this address on the PayPal site.
    /// </summary>
    public static readonly OrderApplicationContextShippingPreference SetProvidedAddress = new("SET_PROVIDED_ADDRESS");

    public static OrderApplicationContextShippingPreference FromValue(string value) => FromValueCore(value);
}
