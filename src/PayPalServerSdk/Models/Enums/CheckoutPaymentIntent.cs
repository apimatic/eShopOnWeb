using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The intent to either capture payment immediately or authorize a payment for an order after order creation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CheckoutPaymentIntent>))]
public sealed record CheckoutPaymentIntent : OpenStringEnum<CheckoutPaymentIntent>
{
    private CheckoutPaymentIntent(string value) : base(value)
    {
    }

    /// <summary>
    /// The merchant intends to capture payment immediately after the customer makes a payment.
    /// </summary>
    public static readonly CheckoutPaymentIntent Capture = new("CAPTURE");

    /// <summary>
    /// The merchant intends to authorize a payment and place funds on hold after the customer makes a payment. Authorized payments are best captured within three days of authorization but are available to capture for up to 29 days. After the three-day honor period, the original authorized payment expires and you must re-authorize the payment. You must make a separate request to capture payments on demand. This intent is not supported when you have more than one <c>purchase_unit</c> within your order.
    /// </summary>
    public static readonly CheckoutPaymentIntent Authorize = new("AUTHORIZE");

    public TResult Match<TResult>(Func<TResult> onCapture,
        Func<TResult> onAuthorize,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Capture => onCapture(),
            _ when this == Authorize => onAuthorize(),
            _ => otherwise(Value)
        };

    public void Match(Action onCapture, Action onAuthorize, Action<string> otherwise)
    {
        if (this == Capture) onCapture();
        else if (this == Authorize) onAuthorize();
        else otherwise(Value);
    }
}
