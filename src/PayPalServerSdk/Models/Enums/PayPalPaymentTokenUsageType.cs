using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The usage type associated with the PayPal payment token., The usage type associated with a digital wallet payment token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalPaymentTokenUsageType>))]
public sealed record PayPalPaymentTokenUsageType : StringEnum<PayPalPaymentTokenUsageType>
{
    private PayPalPaymentTokenUsageType(string value) : base(value)
    {
    }

    /// <summary>
    /// The PayPal Payment Token will be used for future transaction directly with a merchant.
    /// </summary>
    public static readonly PayPalPaymentTokenUsageType Merchant = new("MERCHANT");

    /// <summary>
    /// The PayPal Payment Token will be used for future transaction on a platform. A platform is typically a marketplace or a channel that a payer can purchase goods and services from multiple merchants.
    /// </summary>
    public static readonly PayPalPaymentTokenUsageType Platform = new("PLATFORM");

    public static PayPalPaymentTokenUsageType FromValue(string value) => FromValueCore(value);
}
