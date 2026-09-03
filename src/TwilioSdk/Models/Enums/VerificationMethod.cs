using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification method.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationMethod>))]
public sealed record VerificationMethod : StringEnum<VerificationMethod>
{
    private VerificationMethod(string value) : base(value)
    {
    }

    public static readonly VerificationMethod Sms = new("sms");

    public static readonly VerificationMethod Voice = new("voice");

    public static VerificationMethod FromValue(string value) => FromValueCore(value);
}
