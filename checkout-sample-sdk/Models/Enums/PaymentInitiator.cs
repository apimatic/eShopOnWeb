using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The person or party who initiated or triggered the payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentInitiator>))]
public sealed record PaymentInitiator : StringEnum<PaymentInitiator>
{
    private PaymentInitiator(string value) : base(value)
    {
    }

    /// <summary>
    /// Payment is initiated with the active engagement of the customer. e.g. a customer checking out on a merchant website.
    /// </summary>
    public static readonly PaymentInitiator Customer = new("CUSTOMER");

    /// <summary>
    /// Payment is initiated by merchant on behalf of the customer without the active engagement of customer. e.g. a merchant charging the monthly payment of a subscription to the customer.
    /// </summary>
    public static readonly PaymentInitiator Merchant = new("MERCHANT");

    public static PaymentInitiator FromValue(string value) => FromValueCore(value);
}
