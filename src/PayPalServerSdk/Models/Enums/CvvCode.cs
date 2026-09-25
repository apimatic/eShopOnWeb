using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The card verification value code for for Visa, Discover, Mastercard, or American Express.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CvvCode>))]
public sealed record CvvCode : OpenStringEnum<CvvCode>
{
    private CvvCode(string value) : base(value)
    {
    }

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, error - unrecognized or unknown response.
    /// </summary>
    public static readonly CvvCode E = new("E");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, invalid or null.
    /// </summary>
    public static readonly CvvCode I = new("I");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, the CVV2/CSC matches.
    /// </summary>
    public static readonly CvvCode M = new("M");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, the CVV2/CSC does not match.
    /// </summary>
    public static readonly CvvCode N = new("N");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, it was not processed.
    /// </summary>
    public static readonly CvvCode P = new("P");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, the service is not supported.
    /// </summary>
    public static readonly CvvCode S = new("S");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, unknown - the issuer is not certified.
    /// </summary>
    public static readonly CvvCode U = new("U");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, no response. For Maestro, the service is not available.
    /// </summary>
    public static readonly CvvCode X = new("X");

    /// <summary>
    /// For Visa, Mastercard, Discover, or American Express, error.
    /// </summary>
    public static readonly CvvCode AllOthers = new("All others");

    /// <summary>
    /// For Maestro, the CVV2 matched.
    /// </summary>
    public static readonly CvvCode _0 = new("0");

    /// <summary>
    /// For Maestro, the CVV2 did not match.
    /// </summary>
    public static readonly CvvCode _1 = new("1");

    /// <summary>
    /// For Maestro, the merchant has not implemented CVV2 code handling.
    /// </summary>
    public static readonly CvvCode _2 = new("2");

    /// <summary>
    /// For Maestro, the merchant has indicated that CVV2 is not present on card.
    /// </summary>
    public static readonly CvvCode _3 = new("3");

    /// <summary>
    /// For Maestro, the service is not available.
    /// </summary>
    public static readonly CvvCode _4 = new("4");

    public TResult Match<TResult>(Func<TResult> onE,
        Func<TResult> onI,
        Func<TResult> onM,
        Func<TResult> onN,
        Func<TResult> onP,
        Func<TResult> onS,
        Func<TResult> onU,
        Func<TResult> onX,
        Func<TResult> onAllOthers,
        Func<TResult> on_0,
        Func<TResult> on_1,
        Func<TResult> on_2,
        Func<TResult> on_3,
        Func<TResult> on_4,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == E => onE(),
            _ when this == I => onI(),
            _ when this == M => onM(),
            _ when this == N => onN(),
            _ when this == P => onP(),
            _ when this == S => onS(),
            _ when this == U => onU(),
            _ when this == X => onX(),
            _ when this == AllOthers => onAllOthers(),
            _ when this == _0 => on_0(),
            _ when this == _1 => on_1(),
            _ when this == _2 => on_2(),
            _ when this == _3 => on_3(),
            _ when this == _4 => on_4(),
            _ => otherwise(Value)
        };

    public void Match(Action onE,
        Action onI,
        Action onM,
        Action onN,
        Action onP,
        Action onS,
        Action onU,
        Action onX,
        Action onAllOthers,
        Action on_0,
        Action on_1,
        Action on_2,
        Action on_3,
        Action on_4,
        Action<string> otherwise)
    {
        if (this == E) onE();
        else if (this == I) onI();
        else if (this == M) onM();
        else if (this == N) onN();
        else if (this == P) onP();
        else if (this == S) onS();
        else if (this == U) onU();
        else if (this == X) onX();
        else if (this == AllOthers) onAllOthers();
        else if (this == _0) on_0();
        else if (this == _1) on_1();
        else if (this == _2) on_2();
        else if (this == _3) on_3();
        else if (this == _4) on_4();
        else otherwise(Value);
    }
}
