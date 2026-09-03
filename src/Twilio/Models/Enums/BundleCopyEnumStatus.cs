using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The verification status of the Bundle resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BundleCopyEnumStatus>))]
public sealed record BundleCopyEnumStatus : StringEnum<BundleCopyEnumStatus>
{
    private BundleCopyEnumStatus(string value) : base(value)
    {
    }

    public static readonly BundleCopyEnumStatus Draft = new("draft");

    public static readonly BundleCopyEnumStatus PendingReview = new("pending-review");

    public static readonly BundleCopyEnumStatus InReview = new("in-review");

    public static readonly BundleCopyEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly BundleCopyEnumStatus TwilioApproved = new("twilio-approved");

    public static readonly BundleCopyEnumStatus ProvisionallyApproved = new("provisionally-approved");

    public static BundleCopyEnumStatus FromValue(string value) => FromValueCore(value);
}
