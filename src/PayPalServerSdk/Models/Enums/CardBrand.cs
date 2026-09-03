using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The card network or brand. Applies to credit, debit, gift, and payment cards.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardBrand>))]
public sealed record CardBrand : StringEnum<CardBrand>
{
    private CardBrand(string value) : base(value)
    {
    }

    /// <summary>
    /// Visa card.
    /// </summary>
    public static readonly CardBrand Visa = new("VISA");

    /// <summary>
    /// Mastercard card.
    /// </summary>
    public static readonly CardBrand Mastercard = new("MASTERCARD");

    /// <summary>
    /// Discover card.
    /// </summary>
    public static readonly CardBrand Discover = new("DISCOVER");

    /// <summary>
    /// American Express card.
    /// </summary>
    public static readonly CardBrand Amex = new("AMEX");

    /// <summary>
    /// Solo debit card.
    /// </summary>
    public static readonly CardBrand Solo = new("SOLO");

    /// <summary>
    /// Japan Credit Bureau card.
    /// </summary>
    public static readonly CardBrand Jcb = new("JCB");

    /// <summary>
    /// Military Star card.
    /// </summary>
    public static readonly CardBrand Star = new("STAR");

    /// <summary>
    /// Delta Airlines card.
    /// </summary>
    public static readonly CardBrand Delta = new("DELTA");

    /// <summary>
    /// Switch credit card.
    /// </summary>
    public static readonly CardBrand Switch = new("SWITCH");

    /// <summary>
    /// Maestro credit card.
    /// </summary>
    public static readonly CardBrand Maestro = new("MAESTRO");

    /// <summary>
    /// Carte Bancaire (CB) credit card.
    /// </summary>
    public static readonly CardBrand CbNationale = new("CB_NATIONALE");

    /// <summary>
    /// Configoga credit card.
    /// </summary>
    public static readonly CardBrand Configoga = new("CONFIGOGA");

    /// <summary>
    /// Confidis credit card.
    /// </summary>
    public static readonly CardBrand Confidis = new("CONFIDIS");

    /// <summary>
    /// Visa Electron credit card.
    /// </summary>
    public static readonly CardBrand Electron = new("ELECTRON");

    /// <summary>
    /// Cetelem credit card.
    /// </summary>
    public static readonly CardBrand Cetelem = new("CETELEM");

    /// <summary>
    /// China union pay credit card.
    /// </summary>
    public static readonly CardBrand ChinaUnionPay = new("CHINA_UNION_PAY");

    /// <summary>
    /// The Diners Club International banking and payment services capability network owned by Discover Financial Services (DFS), one of the most recognized brands in US financial services.
    /// </summary>
    public static readonly CardBrand Diners = new("DINERS");

    /// <summary>
    /// The Brazilian Elo card payment network.
    /// </summary>
    public static readonly CardBrand Elo = new("ELO");

    /// <summary>
    /// The Hiper - Ingenico ePayment network.
    /// </summary>
    public static readonly CardBrand Hiper = new("HIPER");

    /// <summary>
    /// The Brazilian Hipercard payment network that's widely accepted in the retail market.
    /// </summary>
    public static readonly CardBrand Hipercard = new("HIPERCARD");

    /// <summary>
    /// The RuPay payment network.
    /// </summary>
    public static readonly CardBrand Rupay = new("RUPAY");

    /// <summary>
    /// The GE Credit Union 3Point card payment network.
    /// </summary>
    public static readonly CardBrand Ge = new("GE");

    /// <summary>
    /// The Synchrony Financial (SYF) payment network.
    /// </summary>
    public static readonly CardBrand Synchrony = new("SYNCHRONY");

    /// <summary>
    /// The Electronic Fund Transfer At Point of Sale(EFTPOS) Debit card payment network.
    /// </summary>
    public static readonly CardBrand Eftpos = new("EFTPOS");

    /// <summary>
    /// The Carte Bancaire payment network.
    /// </summary>
    public static readonly CardBrand CarteBancaire = new("CARTE_BANCAIRE");

    /// <summary>
    /// The Star Access payment network.
    /// </summary>
    public static readonly CardBrand StarAccess = new("STAR_ACCESS");

    /// <summary>
    /// The Pulse payment network.
    /// </summary>
    public static readonly CardBrand Pulse = new("PULSE");

    /// <summary>
    /// The NYCE payment network.
    /// </summary>
    public static readonly CardBrand Nyce = new("NYCE");

    /// <summary>
    /// The Accel payment network.
    /// </summary>
    public static readonly CardBrand Accel = new("ACCEL");

    /// <summary>
    /// UNKNOWN payment network.
    /// </summary>
    public static readonly CardBrand Unknown = new("UNKNOWN");

    public static CardBrand FromValue(string value) => FromValueCore(value);
}
