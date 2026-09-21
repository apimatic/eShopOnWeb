using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// A string specifying the conversion status of the verification. A conversion happens when the user is able to provide the correct code. Possible values are <c>CONVERTED</c> and <c>UNCONVERTED</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationAttemptEnumConversionStatus>))]
public sealed record VerificationAttemptEnumConversionStatus : StringEnum<VerificationAttemptEnumConversionStatus>
{
    private VerificationAttemptEnumConversionStatus(string value) : base(value)
    {
    }

    public static readonly VerificationAttemptEnumConversionStatus Converted = new("converted");

    public static readonly VerificationAttemptEnumConversionStatus Unconverted = new("unconverted");

    public static VerificationAttemptEnumConversionStatus FromValue(string value) => FromValueCore(value);
}
