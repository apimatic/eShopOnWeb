using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The compliance status of the Tollfree Verification record.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumStatus>))]
public sealed record TollfreeVerificationEnumStatus : StringEnum<TollfreeVerificationEnumStatus>
{
    private TollfreeVerificationEnumStatus(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumStatus PendingReview = new("PENDING_REVIEW");

    public static readonly TollfreeVerificationEnumStatus InReview = new("IN_REVIEW");

    public static readonly TollfreeVerificationEnumStatus TwilioApproved = new("TWILIO_APPROVED");

    public static readonly TollfreeVerificationEnumStatus TwilioRejected = new("TWILIO_REJECTED");

    public static TollfreeVerificationEnumStatus FromValue(string value) => FromValueCore(value);
}
