using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The vault that stores the payment profile with the provided <c>vault_token</c>. Use <c>bogus</c> for testing.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CreditCardVault>))]
public sealed record CreditCardVault : StringEnum<CreditCardVault>
{
    private CreditCardVault(string value) : base(value)
    {
    }

    public static readonly CreditCardVault Adyen = new("adyen");

    public static readonly CreditCardVault Authorizenet = new("authorizenet");

    public static readonly CreditCardVault Beanstream = new("beanstream");

    public static readonly CreditCardVault BlueSnap = new("blue_snap");

    public static readonly CreditCardVault Bogus = new("bogus");

    public static readonly CreditCardVault Braintree1 = new("braintree1");

    public static readonly CreditCardVault BraintreeBlue = new("braintree_blue");

    public static readonly CreditCardVault Checkout = new("checkout");

    public static readonly CreditCardVault Cybersource = new("cybersource");

    public static readonly CreditCardVault Elavon = new("elavon");

    public static readonly CreditCardVault Eway = new("eway");

    public static readonly CreditCardVault EwayRapid = new("eway_rapid");

    public static readonly CreditCardVault EwayRapidStd = new("eway_rapid_std");

    public static readonly CreditCardVault Firstdata = new("firstdata");

    public static readonly CreditCardVault Forte = new("forte");

    public static readonly CreditCardVault Litle = new("litle");

    public static readonly CreditCardVault MaxioPayments = new("maxio_payments");

    public static readonly CreditCardVault Maxp = new("maxp");

    public static readonly CreditCardVault Moduslink = new("moduslink");

    public static readonly CreditCardVault Moneris = new("moneris");

    public static readonly CreditCardVault Nmi = new("nmi");

    public static readonly CreditCardVault Orbital = new("orbital");

    public static readonly CreditCardVault PaymentExpress = new("payment_express");

    public static readonly CreditCardVault Paymill = new("paymill");

    public static readonly CreditCardVault Paypal = new("paypal");

    public static readonly CreditCardVault PaypalComplete = new("paypal_complete");

    public static readonly CreditCardVault Pin = new("pin");

    public static readonly CreditCardVault Square = new("square");

    public static readonly CreditCardVault Stripe = new("stripe");

    public static readonly CreditCardVault StripeConnect = new("stripe_connect");

    public static readonly CreditCardVault TrustCommerce = new("trust_commerce");

    public static readonly CreditCardVault Unipaas = new("unipaas");

    public static readonly CreditCardVault Wirecard = new("wirecard");

    public static CreditCardVault FromValue(string value) => FromValueCore(value);
}
