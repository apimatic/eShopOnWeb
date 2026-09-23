using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The intent to either capture payment immediately or authorize a payment for an order after order creation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CheckoutPaymentIntent>))]
public sealed record CheckoutPaymentIntent : StringEnum<CheckoutPaymentIntent>
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

    public static CheckoutPaymentIntent FromValue(string value) => FromValueCore(value);
}
