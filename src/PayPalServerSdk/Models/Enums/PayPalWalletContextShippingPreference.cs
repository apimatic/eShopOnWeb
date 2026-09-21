using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The location from which the shipping address is derived.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalWalletContextShippingPreference>))]
public sealed record PayPalWalletContextShippingPreference : StringEnum<PayPalWalletContextShippingPreference>
{
    private PayPalWalletContextShippingPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// Get the customer-provided shipping address on the PayPal site.
    /// </summary>
    public static readonly PayPalWalletContextShippingPreference GetFromFile = new("GET_FROM_FILE");

    /// <summary>
    /// Removes the shipping address information from the API response and the Paypal site. However, the shipping.phone_number and shipping.email_address fields will still be returned to allow for digital goods delivery.
    /// </summary>
    public static readonly PayPalWalletContextShippingPreference NoShipping = new("NO_SHIPPING");

    /// <summary>
    /// Get the merchant-provided address. The customer cannot change this address on the PayPal site. If merchant does not pass an address, customer can choose the address on PayPal pages.
    /// </summary>
    public static readonly PayPalWalletContextShippingPreference SetProvidedAddress = new("SET_PROVIDED_ADDRESS");

    public static PayPalWalletContextShippingPreference FromValue(string value) => FromValueCore(value);
}
