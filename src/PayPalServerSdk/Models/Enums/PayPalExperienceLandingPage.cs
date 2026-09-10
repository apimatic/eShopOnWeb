using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The type of landing page to show on the PayPal site for customer checkout.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalExperienceLandingPage>))]
public sealed record PayPalExperienceLandingPage : StringEnum<PayPalExperienceLandingPage>
{
    private PayPalExperienceLandingPage(string value) : base(value)
    {
    }

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to a page to log in to PayPal and approve the payment.
    /// </summary>
    public static readonly PayPalExperienceLandingPage Login = new("LOGIN");

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to a page to enter credit or debit card and other relevant billing information required to complete the purchase. This option has previously been also called as 'BILLING'
    /// </summary>
    public static readonly PayPalExperienceLandingPage GuestCheckout = new("GUEST_CHECKOUT");

    /// <summary>
    /// When the customer clicks PayPal Checkout, the customer is redirected to either a page to log in to PayPal and approve the payment or to a page to enter credit or debit card and other relevant billing information required to complete the purchase, depending on their previous interaction with PayPal.
    /// </summary>
    public static readonly PayPalExperienceLandingPage NoPreference = new("NO_PREFERENCE");

    /// <summary>
    /// DEPRECATED - please use GUEST_CHECKOUT. All implementations of 'BILLING' will be routed to 'GUEST_CHECKOUT'. When the customer clicks PayPal Checkout, the customer is redirected to a page to enter credit or debit card and other relevant billing information required to complete the purchase.
    /// </summary>
    public static readonly PayPalExperienceLandingPage Billing = new("BILLING");

    public static PayPalExperienceLandingPage FromValue(string value) => FromValueCore(value);
}
