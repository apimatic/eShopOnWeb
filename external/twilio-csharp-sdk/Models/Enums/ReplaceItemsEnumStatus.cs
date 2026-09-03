using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The verification status of the Bundle resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReplaceItemsEnumStatus>))]
public sealed record ReplaceItemsEnumStatus : StringEnum<ReplaceItemsEnumStatus>
{
    private ReplaceItemsEnumStatus(string value) : base(value)
    {
    }

    public static readonly ReplaceItemsEnumStatus Draft = new("draft");

    public static readonly ReplaceItemsEnumStatus PendingReview = new("pending-review");

    public static readonly ReplaceItemsEnumStatus InReview = new("in-review");

    public static readonly ReplaceItemsEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly ReplaceItemsEnumStatus TwilioApproved = new("twilio-approved");

    public static readonly ReplaceItemsEnumStatus ProvisionallyApproved = new("provisionally-approved");

    public static ReplaceItemsEnumStatus FromValue(string value) => FromValueCore(value);
}
