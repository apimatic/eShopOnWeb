using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The address verification code for Visa, Discover, Mastercard, or American Express transactions.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AvsCode>))]
public sealed record AvsCode : OpenStringEnum<AvsCode>
{
    private AvsCode(string value) : base(value)
    {
    }

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address matches but the zip code does not match. For American Express transactions, the card holder address is correct.
    /// </summary>
    public static readonly AvsCode A = new("A");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address matches. International A.
    /// </summary>
    public static readonly AvsCode B = new("B");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, no values match. International N.
    /// </summary>
    public static readonly AvsCode C = new("C");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address and postal code match. International X.
    /// </summary>
    public static readonly AvsCode D = new("D");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, not allowed for Internet or phone transactions. For American Express card holder, the name is incorrect but the address and postal code match.
    /// </summary>
    public static readonly AvsCode E = new("E");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address and postal code match. UK-specific X. For American Express card holder, the name is incorrect but the address matches.
    /// </summary>
    public static readonly AvsCode F = new("F");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, global is unavailable. Nothing matches.
    /// </summary>
    public static readonly AvsCode G = new("G");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, international is unavailable. Not applicable.
    /// </summary>
    public static readonly AvsCode I = new("I");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address and postal code match. For American Express card holder, the name, address, and postal code match.
    /// </summary>
    public static readonly AvsCode M = new("M");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, nothing matches. For American Express card holder, the address and postal code are both incorrect.
    /// </summary>
    public static readonly AvsCode N = new("N");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, postal international Z. Postal code only.
    /// </summary>
    public static readonly AvsCode P = new("P");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, re-try the request. For American Express, the system is unavailable.
    /// </summary>
    public static readonly AvsCode R = new("R");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, the service is not supported.
    /// </summary>
    public static readonly AvsCode S = new("S");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the service is unavailable. For American Express, information is not available. For Maestro, the address is not checked or the acquirer had no response. The service is not available.
    /// </summary>
    public static readonly AvsCode U = new("U");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, whole ZIP code. For American Express, the card holder name, address, and postal code are all incorrect.
    /// </summary>
    public static readonly AvsCode W = new("W");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, exact match of the address and the nine-digit ZIP code. For American Express, the card holder name, address, and postal code are all incorrect.
    /// </summary>
    public static readonly AvsCode X = new("X");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the address and five-digit ZIP code match. For American Express, the card holder address and postal code are both correct.
    /// </summary>
    public static readonly AvsCode Y = new("Y");

    /// <summary>
    /// For Visa, Mastercard, or Discover transactions, the five-digit ZIP code matches but no address. For American Express, only the card holder postal code is correct.
    /// </summary>
    public static readonly AvsCode Z = new("Z");

    /// <summary>
    /// For Maestro, no AVS response was obtained.
    /// </summary>
    public static readonly AvsCode Null = new("Null");

    /// <summary>
    /// For Maestro, all address information matches.
    /// </summary>
    public static readonly AvsCode _0 = new("0");

    /// <summary>
    /// For Maestro, none of the address information matches.
    /// </summary>
    public static readonly AvsCode _1 = new("1");

    /// <summary>
    /// For Maestro, part of the address information matches.
    /// </summary>
    public static readonly AvsCode _2 = new("2");

    /// <summary>
    /// For Maestro, the merchant did not provide AVS information. It was not processed.
    /// </summary>
    public static readonly AvsCode _3 = new("3");

    /// <summary>
    /// For Maestro, the address was not checked or the acquirer had no response. The service is not available.
    /// </summary>
    public static readonly AvsCode _4 = new("4");

    public TResult Match<TResult>(Func<TResult> onA,
        Func<TResult> onB,
        Func<TResult> onC,
        Func<TResult> onD,
        Func<TResult> onE,
        Func<TResult> onF,
        Func<TResult> onG,
        Func<TResult> onI,
        Func<TResult> onM,
        Func<TResult> onN,
        Func<TResult> onP,
        Func<TResult> onR,
        Func<TResult> onS,
        Func<TResult> onU,
        Func<TResult> onW,
        Func<TResult> onX,
        Func<TResult> onY,
        Func<TResult> onZ,
        Func<TResult> onNull,
        Func<TResult> on_0,
        Func<TResult> on_1,
        Func<TResult> on_2,
        Func<TResult> on_3,
        Func<TResult> on_4,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == A => onA(),
            _ when this == B => onB(),
            _ when this == C => onC(),
            _ when this == D => onD(),
            _ when this == E => onE(),
            _ when this == F => onF(),
            _ when this == G => onG(),
            _ when this == I => onI(),
            _ when this == M => onM(),
            _ when this == N => onN(),
            _ when this == P => onP(),
            _ when this == R => onR(),
            _ when this == S => onS(),
            _ when this == U => onU(),
            _ when this == W => onW(),
            _ when this == X => onX(),
            _ when this == Y => onY(),
            _ when this == Z => onZ(),
            _ when this == Null => onNull(),
            _ when this == _0 => on_0(),
            _ when this == _1 => on_1(),
            _ when this == _2 => on_2(),
            _ when this == _3 => on_3(),
            _ when this == _4 => on_4(),
            _ => otherwise(Value)
        };

    public void Match(Action onA,
        Action onB,
        Action onC,
        Action onD,
        Action onE,
        Action onF,
        Action onG,
        Action onI,
        Action onM,
        Action onN,
        Action onP,
        Action onR,
        Action onS,
        Action onU,
        Action onW,
        Action onX,
        Action onY,
        Action onZ,
        Action onNull,
        Action on_0,
        Action on_1,
        Action on_2,
        Action on_3,
        Action on_4,
        Action<string> otherwise)
    {
        if (this == A) onA();
        else if (this == B) onB();
        else if (this == C) onC();
        else if (this == D) onD();
        else if (this == E) onE();
        else if (this == F) onF();
        else if (this == G) onG();
        else if (this == I) onI();
        else if (this == M) onM();
        else if (this == N) onN();
        else if (this == P) onP();
        else if (this == R) onR();
        else if (this == S) onS();
        else if (this == U) onU();
        else if (this == W) onW();
        else if (this == X) onX();
        else if (this == Y) onY();
        else if (this == Z) onZ();
        else if (this == Null) onNull();
        else if (this == _0) on_0();
        else if (this == _1) on_1();
        else if (this == _2) on_2();
        else if (this == _3) on_3();
        else if (this == _4) on_4();
        else otherwise(Value);
    }
}
