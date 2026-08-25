using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The card network or brand. Applies to credit, debit, gift, and payment cards.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionsCardBrand>))]
public sealed record SubscriptionsCardBrand : StringEnum<SubscriptionsCardBrand>
{
    private SubscriptionsCardBrand(string value) : base(value)
    {
    }

    /// <summary>
    /// Visa card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Visa = new("VISA");

    /// <summary>
    /// Mastercard card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Mastercard = new("MASTERCARD");

    /// <summary>
    /// Discover card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Discover = new("DISCOVER");

    /// <summary>
    /// American Express card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Amex = new("AMEX");

    /// <summary>
    /// Solo debit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Solo = new("SOLO");

    /// <summary>
    /// Japan Credit Bureau card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Jcb = new("JCB");

    /// <summary>
    /// Military Star card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Star = new("STAR");

    /// <summary>
    /// Delta Airlines card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Delta = new("DELTA");

    /// <summary>
    /// Switch credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Switch = new("SWITCH");

    /// <summary>
    /// Maestro credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Maestro = new("MAESTRO");

    /// <summary>
    /// Carte Bancaire (CB) credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand CbNationale = new("CB_NATIONALE");

    /// <summary>
    /// Configoga credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Configoga = new("CONFIGOGA");

    /// <summary>
    /// Confidis credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Confidis = new("CONFIDIS");

    /// <summary>
    /// Visa Electron credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Electron = new("ELECTRON");

    /// <summary>
    /// Cetelem credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand Cetelem = new("CETELEM");

    /// <summary>
    /// China union pay credit card.
    /// </summary>
    public static readonly SubscriptionsCardBrand ChinaUnionPay = new("CHINA_UNION_PAY");

    /// <summary>
    /// The Diners Club International banking and payment services capability network owned by Discover Financial Services (DFS), one of the most recognized brands in US financial services.
    /// </summary>
    public static readonly SubscriptionsCardBrand Diners = new("DINERS");

    /// <summary>
    /// The Brazilian Elo card payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Elo = new("ELO");

    /// <summary>
    /// The Hiper - Ingenico ePayment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Hiper = new("HIPER");

    /// <summary>
    /// The Brazilian Hipercard payment network that's widely accepted in the retail market.
    /// </summary>
    public static readonly SubscriptionsCardBrand Hipercard = new("HIPERCARD");

    /// <summary>
    /// The RuPay payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Rupay = new("RUPAY");

    /// <summary>
    /// The GE Credit Union 3Point card payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Ge = new("GE");

    /// <summary>
    /// The Synchrony Financial (SYF) payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Synchrony = new("SYNCHRONY");

    /// <summary>
    /// The Electronic Fund Transfer At Point of Sale(EFTPOS) Debit card payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Eftpos = new("EFTPOS");

    /// <summary>
    /// UNKNOWN payment network.
    /// </summary>
    public static readonly SubscriptionsCardBrand Unknown = new("UNKNOWN");

    public static SubscriptionsCardBrand FromValue(string value) => FromValueCore(value);
}
