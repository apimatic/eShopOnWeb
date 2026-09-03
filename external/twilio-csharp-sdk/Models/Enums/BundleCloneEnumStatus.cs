using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The verification status of the Bundle resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BundleCloneEnumStatus>))]
public sealed record BundleCloneEnumStatus : StringEnum<BundleCloneEnumStatus>
{
    private BundleCloneEnumStatus(string value) : base(value)
    {
    }

    public static readonly BundleCloneEnumStatus Draft = new("draft");

    public static readonly BundleCloneEnumStatus PendingReview = new("pending-review");

    public static readonly BundleCloneEnumStatus InReview = new("in-review");

    public static readonly BundleCloneEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly BundleCloneEnumStatus TwilioApproved = new("twilio-approved");

    public static readonly BundleCloneEnumStatus ProvisionallyApproved = new("provisionally-approved");

    public static BundleCloneEnumStatus FromValue(string value) => FromValueCore(value);
}
