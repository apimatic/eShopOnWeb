using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// DEPRECATED. Configures a Continue or Pay Now checkout flow.  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.user_action</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OrderApplicationContextUserAction>))]
public sealed record OrderApplicationContextUserAction : StringEnum<OrderApplicationContextUserAction>
{
    private OrderApplicationContextUserAction(string value) : base(value)
    {
    }

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Continue button appears. Use this option when the final amount is not known when the checkout flow is initiated and you want to redirect the customer to the merchant page without processing the payment.
    /// </summary>
    public static readonly OrderApplicationContextUserAction Continue = new("CONTINUE");

    /// <summary>
    /// After you redirect the customer to the PayPal payment page, a Pay Now button appears. Use this option when the final amount is known when the checkout is initiated and you want to process the payment immediately when the customer clicks Pay Now.
    /// </summary>
    public static readonly OrderApplicationContextUserAction PayNow = new("PAY_NOW");

    public static OrderApplicationContextUserAction FromValue(string value) => FromValueCore(value);
}
