using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The preference to display the contact information (buyer’s shipping email &amp; phone number) on PayPal's checkout for easy merchant-buyer communication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalWalletContactPreference>))]
public sealed record PayPalWalletContactPreference : StringEnum<PayPalWalletContactPreference>
{
    private PayPalWalletContactPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// The merchant can opt out of showing buyer's contact information on PayPal checkout.
    /// </summary>
    public static readonly PayPalWalletContactPreference NoContactInfo = new("NO_CONTACT_INFO");

    /// <summary>
    /// The merchant allows buyer to add or update shipping contact information on the PayPal checkout. Please ensure to use this updated information returned in shipping.email_address and shipping.phone_number to contact your buyers.
    /// </summary>
    public static readonly PayPalWalletContactPreference UpdateContactInfo = new("UPDATE_CONTACT_INFO");

    /// <summary>
    /// The buyer can only see but can not override merchant passed contact information (shipping.email_address and shipping.phone_number) on PayPal checkout. NOTE: If you don't pass the contact information, the behavior is the same as NO_CONTACT_INFO preference.
    /// </summary>
    public static readonly PayPalWalletContactPreference RetainContactInfo = new("RETAIN_CONTACT_INFO");

    public static PayPalWalletContactPreference FromValue(string value) => FromValueCore(value);
}
