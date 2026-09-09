using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The customer type associated with the PayPal payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer., The customer type associated with a digital wallet payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalPaymentTokenCustomerType>))]
public sealed record PayPalPaymentTokenCustomerType : StringEnum<PayPalPaymentTokenCustomerType>
{
    private PayPalPaymentTokenCustomerType(string value) : base(value)
    {
    }

    /// <summary>
    /// The customer vaulting the PayPal payment token is a consumer on the merchant / platform.
    /// </summary>
    public static readonly PayPalPaymentTokenCustomerType Consumer = new("CONSUMER");

    /// <summary>
    /// The customer vaulting the PayPal payment token is a business on merchant / platform.
    /// </summary>
    public static readonly PayPalPaymentTokenCustomerType Business = new("BUSINESS");

    public static PayPalPaymentTokenCustomerType FromValue(string value) => FromValueCore(value);
}
