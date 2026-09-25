using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The customer type associated with the PayPal payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer., The customer type associated with a digital wallet payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalPaymentTokenCustomerType>))]
public sealed record PayPalPaymentTokenCustomerType : OpenStringEnum<PayPalPaymentTokenCustomerType>
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

    public TResult Match<TResult>(Func<TResult> onConsumer,
        Func<TResult> onBusiness,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Consumer => onConsumer(),
            _ when this == Business => onBusiness(),
            _ => otherwise(Value)
        };

    public void Match(Action onConsumer, Action onBusiness, Action<string> otherwise)
    {
        if (this == Consumer) onConsumer();
        else if (this == Business) onBusiness();
        else otherwise(Value);
    }
}
