using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification status of the Bundle resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BundleEnumStatus>))]
public sealed record BundleEnumStatus : StringEnum<BundleEnumStatus>
{
    private BundleEnumStatus(string value) : base(value)
    {
    }

    public static readonly BundleEnumStatus Draft = new("draft");

    public static readonly BundleEnumStatus PendingReview = new("pending-review");

    public static readonly BundleEnumStatus InReview = new("in-review");

    public static readonly BundleEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly BundleEnumStatus TwilioApproved = new("twilio-approved");

    public static readonly BundleEnumStatus ProvisionallyApproved = new("provisionally-approved");

    public static BundleEnumStatus FromValue(string value) => FromValueCore(value);
}
