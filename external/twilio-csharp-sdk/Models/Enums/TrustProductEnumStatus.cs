using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The verification status of the Trust Product resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TrustProductEnumStatus>))]
public sealed record TrustProductEnumStatus : StringEnum<TrustProductEnumStatus>
{
    private TrustProductEnumStatus(string value) : base(value)
    {
    }

    public static readonly TrustProductEnumStatus Draft = new("draft");

    public static readonly TrustProductEnumStatus PendingReview = new("pending-review");

    public static readonly TrustProductEnumStatus InReview = new("in-review");

    public static readonly TrustProductEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly TrustProductEnumStatus TwilioApproved = new("twilio-approved");

    public static TrustProductEnumStatus FromValue(string value) => FromValueCore(value);
}
