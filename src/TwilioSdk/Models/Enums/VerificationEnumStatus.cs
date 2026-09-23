using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the verification. Can be: <c>pending</c>, <c>approved</c>, <c>canceled</c>, <c>max_attempts_reached</c>, <c>deleted</c>, <c>failed</c> or <c>expired</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationEnumStatus>))]
public sealed record VerificationEnumStatus : StringEnum<VerificationEnumStatus>
{
    private VerificationEnumStatus(string value) : base(value)
    {
    }

    public static readonly VerificationEnumStatus Canceled = new("canceled");

    public static readonly VerificationEnumStatus Approved = new("approved");

    public static VerificationEnumStatus FromValue(string value) => FromValueCore(value);
}
