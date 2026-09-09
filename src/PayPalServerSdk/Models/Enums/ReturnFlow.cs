using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Merchant preference on how the buyer can navigate back to merchant website post approving the transaction on the Venmo App.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReturnFlow>))]
public sealed record ReturnFlow : StringEnum<ReturnFlow>
{
    private ReturnFlow(string value) : base(value)
    {
    }

    /// <summary>
    /// After payment approval in the PayPal App, buyer will automatically be redirected to the merchant website.
    /// </summary>
    public static readonly ReturnFlow Auto = new("AUTO");

    /// <summary>
    /// After payment approval in the PayPal App, buyer will be asked to manually navigate back to the merchant website where they started the transaction from. The buyer is shown a message like 'Return to Merchant' to return to the source where the transaction actually started.
    /// </summary>
    public static readonly ReturnFlow Manual = new("MANUAL");

    public static ReturnFlow FromValue(string value) => FromValueCore(value);
}
