using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The address verification code for Visa, Discover, Mastercard, or American Express transactions.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AvsCode>))]
public sealed record AvsCode : StringEnum<AvsCode>
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

    public static AvsCode FromValue(string value) => FromValueCore(value);
}
