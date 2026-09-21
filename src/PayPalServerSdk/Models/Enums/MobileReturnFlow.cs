using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Merchant preference on how the buyer can navigate back to merchant website post approving the transaction on the PayPal App.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MobileReturnFlow>))]
public sealed record MobileReturnFlow : StringEnum<MobileReturnFlow>
{
    private MobileReturnFlow(string value) : base(value)
    {
    }

    /// <summary>
    /// After payment approval in the PayPal App, buyer will automatically be redirected to the merchant website.
    /// </summary>
    public static readonly MobileReturnFlow Auto = new("AUTO");

    /// <summary>
    /// After payment approval in the PayPal App, buyer will be asked to manually navigate back to the merchant website where they started the transaction from. The buyer is shown a message like 'Return to Merchant' to return to the source where the transaction actually started.
    /// </summary>
    public static readonly MobileReturnFlow Manual = new("MANUAL");

    public static MobileReturnFlow FromValue(string value) => FromValueCore(value);
}
