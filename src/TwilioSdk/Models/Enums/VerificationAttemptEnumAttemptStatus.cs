using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VerificationAttemptEnumAttemptStatus>))]
public sealed record VerificationAttemptEnumAttemptStatus : StringEnum<VerificationAttemptEnumAttemptStatus>
{
    private VerificationAttemptEnumAttemptStatus(string value) : base(value)
    {
    }

    public static readonly VerificationAttemptEnumAttemptStatus Confirmed = new("confirmed");

    public static readonly VerificationAttemptEnumAttemptStatus Unconfirmed = new("unconfirmed");

    public static readonly VerificationAttemptEnumAttemptStatus Expired = new("expired");

    public static VerificationAttemptEnumAttemptStatus FromValue(string value) => FromValueCore(value);
}
