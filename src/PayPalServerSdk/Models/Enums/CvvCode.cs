using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The card verification value code for for Visa, Discover, Mastercard, or American Express.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CvvCode>))]
public sealed record CvvCode : StringEnum<CvvCode>
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

    public static CvvCode FromValue(string value) => FromValueCore(value);
}
