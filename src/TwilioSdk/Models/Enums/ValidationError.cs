using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Contains reasons why a phone number is invalid. Possible values: TOO_SHORT, TOO_LONG, INVALID_BUT_POSSIBLE, INVALID_COUNTRY_CODE, INVALID_LENGTH, NOT_A_NUMBER.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ValidationError>))]
public sealed record ValidationError : StringEnum<ValidationError>
{
    private ValidationError(string value) : base(value)
    {
    }

    public static readonly ValidationError TooShort = new("TOO_SHORT");

    public static readonly ValidationError TooLong = new("TOO_LONG");

    public static readonly ValidationError InvalidButPossible = new("INVALID_BUT_POSSIBLE");

    public static readonly ValidationError InvalidCountryCode = new("INVALID_COUNTRY_CODE");

    public static readonly ValidationError InvalidLength = new("INVALID_LENGTH");

    public static readonly ValidationError NotANumber = new("NOT_A_NUMBER");

    public static ValidationError FromValue(string value) => FromValueCore(value);
}
