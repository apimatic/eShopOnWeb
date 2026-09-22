using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// DEPRECATED. DEPRECATED. The type of landing page to show on the PayPal site for customer checkout.  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.landing_page</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OrderApplicationContextLandingPage>))]
public sealed record OrderApplicationContextLandingPage : StringEnum<OrderApplicationContextLandingPage>
{
    private OrderApplicationContextLandingPage(string value) : base(value)
    {
    }

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to a page to log in to PayPal and approve the payment.
    /// </summary>
    public static readonly OrderApplicationContextLandingPage Login = new("LOGIN");

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to a page to enter credit or debit card and other relevant billing information required to complete the purchase.
    /// </summary>
    public static readonly OrderApplicationContextLandingPage Billing = new("BILLING");

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to either a page to log in to PayPal and approve the payment or to a page to enter credit or debit card and other relevant billing information required to complete the purchase, depending on their previous interaction with PayPal.
    /// </summary>
    public static readonly OrderApplicationContextLandingPage NoPreference = new("NO_PREFERENCE");

    public static OrderApplicationContextLandingPage FromValue(string value) => FromValueCore(value);
}
