using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The location from which the shipping address is derived., The shipping preference. This only applies to PayPal payment source., The shipping preference. This only applies to PayPal payment source., The location from which the shipping address is derived.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ExperienceContextShippingPreference>))]
public sealed record ExperienceContextShippingPreference : StringEnum<ExperienceContextShippingPreference>
{
    private ExperienceContextShippingPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// Get the customer-provided shipping address on the PayPal site.
    /// </summary>
    public static readonly ExperienceContextShippingPreference GetFromFile = new("GET_FROM_FILE");

    /// <summary>
    /// Redacts the shipping address from the PayPal site. Recommended for digital goods.
    /// </summary>
    public static readonly ExperienceContextShippingPreference NoShipping = new("NO_SHIPPING");

    /// <summary>
    /// Merchant sends the shipping address using purchase_units.shipping.address. The customer cannot change this address on the PayPal site.
    /// </summary>
    public static readonly ExperienceContextShippingPreference SetProvidedAddress = new("SET_PROVIDED_ADDRESS");

    public static ExperienceContextShippingPreference FromValue(string value) => FromValueCore(value);
}
