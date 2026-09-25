using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The card network or brand. Applies to credit, debit, gift, and payment cards.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionsCardBrand>))]
public sealed record SubscriptionsCardBrand : OpenStringEnum<SubscriptionsCardBrand>
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

    public TResult Match<TResult>(Func<TResult> onVisa,
        Func<TResult> onMastercard,
        Func<TResult> onDiscover,
        Func<TResult> onAmex,
        Func<TResult> onSolo,
        Func<TResult> onJcb,
        Func<TResult> onStar,
        Func<TResult> onDelta,
        Func<TResult> onSwitch,
        Func<TResult> onMaestro,
        Func<TResult> onCbNationale,
        Func<TResult> onConfigoga,
        Func<TResult> onConfidis,
        Func<TResult> onElectron,
        Func<TResult> onCetelem,
        Func<TResult> onChinaUnionPay,
        Func<TResult> onDiners,
        Func<TResult> onElo,
        Func<TResult> onHiper,
        Func<TResult> onHipercard,
        Func<TResult> onRupay,
        Func<TResult> onGe,
        Func<TResult> onSynchrony,
        Func<TResult> onEftpos,
        Func<TResult> onUnknown,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Visa => onVisa(),
            _ when this == Mastercard => onMastercard(),
            _ when this == Discover => onDiscover(),
            _ when this == Amex => onAmex(),
            _ when this == Solo => onSolo(),
            _ when this == Jcb => onJcb(),
            _ when this == Star => onStar(),
            _ when this == Delta => onDelta(),
            _ when this == Switch => onSwitch(),
            _ when this == Maestro => onMaestro(),
            _ when this == CbNationale => onCbNationale(),
            _ when this == Configoga => onConfigoga(),
            _ when this == Confidis => onConfidis(),
            _ when this == Electron => onElectron(),
            _ when this == Cetelem => onCetelem(),
            _ when this == ChinaUnionPay => onChinaUnionPay(),
            _ when this == Diners => onDiners(),
            _ when this == Elo => onElo(),
            _ when this == Hiper => onHiper(),
            _ when this == Hipercard => onHipercard(),
            _ when this == Rupay => onRupay(),
            _ when this == Ge => onGe(),
            _ when this == Synchrony => onSynchrony(),
            _ when this == Eftpos => onEftpos(),
            _ when this == Unknown => onUnknown(),
            _ => otherwise(Value)
        };

    public void Match(Action onVisa,
        Action onMastercard,
        Action onDiscover,
        Action onAmex,
        Action onSolo,
        Action onJcb,
        Action onStar,
        Action onDelta,
        Action onSwitch,
        Action onMaestro,
        Action onCbNationale,
        Action onConfigoga,
        Action onConfidis,
        Action onElectron,
        Action onCetelem,
        Action onChinaUnionPay,
        Action onDiners,
        Action onElo,
        Action onHiper,
        Action onHipercard,
        Action onRupay,
        Action onGe,
        Action onSynchrony,
        Action onEftpos,
        Action onUnknown,
        Action<string> otherwise)
    {
        if (this == Visa) onVisa();
        else if (this == Mastercard) onMastercard();
        else if (this == Discover) onDiscover();
        else if (this == Amex) onAmex();
        else if (this == Solo) onSolo();
        else if (this == Jcb) onJcb();
        else if (this == Star) onStar();
        else if (this == Delta) onDelta();
        else if (this == Switch) onSwitch();
        else if (this == Maestro) onMaestro();
        else if (this == CbNationale) onCbNationale();
        else if (this == Configoga) onConfigoga();
        else if (this == Confidis) onConfidis();
        else if (this == Electron) onElectron();
        else if (this == Cetelem) onCetelem();
        else if (this == ChinaUnionPay) onChinaUnionPay();
        else if (this == Diners) onDiners();
        else if (this == Elo) onElo();
        else if (this == Hiper) onHiper();
        else if (this == Hipercard) onHipercard();
        else if (this == Rupay) onRupay();
        else if (this == Ge) onGe();
        else if (this == Synchrony) onSynchrony();
        else if (this == Eftpos) onEftpos();
        else if (this == Unknown) onUnknown();
        else otherwise(Value);
    }
}
