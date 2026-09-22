using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The vault that stores the payment profile with the provided vault_token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalVault>))]
public sealed record PayPalVault : StringEnum<PayPalVault>
{
    private PayPalVault(string value) : base(value)
    {
    }

    public static readonly PayPalVault BraintreeBlue = new("braintree_blue");

    public static readonly PayPalVault Paypal = new("paypal");

    public static readonly PayPalVault Moduslink = new("moduslink");

    public static readonly PayPalVault PaypalComplete = new("paypal_complete");

    public static PayPalVault FromValue(string value) => FromValueCore(value);
}
