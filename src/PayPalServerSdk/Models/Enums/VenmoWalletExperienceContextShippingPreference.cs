using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The location from which the shipping address is derived.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VenmoWalletExperienceContextShippingPreference>))]
public sealed record VenmoWalletExperienceContextShippingPreference : StringEnum<VenmoWalletExperienceContextShippingPreference>
{
    private VenmoWalletExperienceContextShippingPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// Get the customer-provided shipping address on the PayPal site.
    /// </summary>
    public static readonly VenmoWalletExperienceContextShippingPreference GetFromFile = new("GET_FROM_FILE");

    /// <summary>
    /// Redacts the shipping address from the PayPal site. Recommended for digital goods.
    /// </summary>
    public static readonly VenmoWalletExperienceContextShippingPreference NoShipping = new("NO_SHIPPING");

    /// <summary>
    /// Get the merchant-provided address. The customer cannot change this address on the PayPal site. If merchant does not pass an address, customer can choose the address on PayPal pages.
    /// </summary>
    public static readonly VenmoWalletExperienceContextShippingPreference SetProvidedAddress = new("SET_PROVIDED_ADDRESS");

    public static VenmoWalletExperienceContextShippingPreference FromValue(string value) =>
        FromValueCore(value);
}
