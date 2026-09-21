using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The vault that stores the payment profile with the provided <c>vault_token</c>. Use <c>bogus</c> for testing.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AllVaults>))]
public sealed record AllVaults : StringEnum<AllVaults>
{
    private AllVaults(string value) : base(value)
    {
    }

    public static readonly AllVaults Adyen = new("adyen");

    public static readonly AllVaults Authorizenet = new("authorizenet");

    public static readonly AllVaults Beanstream = new("beanstream");

    public static readonly AllVaults BlueSnap = new("blue_snap");

    public static readonly AllVaults Bogus = new("bogus");

    public static readonly AllVaults Braintree1 = new("braintree1");

    public static readonly AllVaults BraintreeBlue = new("braintree_blue");

    public static readonly AllVaults Checkout = new("checkout");

    public static readonly AllVaults Cybersource = new("cybersource");

    public static readonly AllVaults Elavon = new("elavon");

    public static readonly AllVaults Eway = new("eway");

    public static readonly AllVaults EwayRapid = new("eway_rapid");

    public static readonly AllVaults EwayRapidStd = new("eway_rapid_std");

    public static readonly AllVaults Firstdata = new("firstdata");

    public static readonly AllVaults Forte = new("forte");

    public static readonly AllVaults Gocardless = new("gocardless");

    public static readonly AllVaults Litle = new("litle");

    public static readonly AllVaults MaxioPayments = new("maxio_payments");

    public static readonly AllVaults Maxp = new("maxp");

    public static readonly AllVaults Moduslink = new("moduslink");

    public static readonly AllVaults Moneris = new("moneris");

    public static readonly AllVaults Nmi = new("nmi");

    public static readonly AllVaults Orbital = new("orbital");

    public static readonly AllVaults PaymentExpress = new("payment_express");

    public static readonly AllVaults Paymill = new("paymill");

    public static readonly AllVaults Paypal = new("paypal");

    public static readonly AllVaults PaypalComplete = new("paypal_complete");

    public static readonly AllVaults Pin = new("pin");

    public static readonly AllVaults Square = new("square");

    public static readonly AllVaults Stripe = new("stripe");

    public static readonly AllVaults StripeConnect = new("stripe_connect");

    public static readonly AllVaults TrustCommerce = new("trust_commerce");

    public static readonly AllVaults Unipaas = new("unipaas");

    public static readonly AllVaults Wirecard = new("wirecard");

    public static AllVaults FromValue(string value) => FromValueCore(value);
}
