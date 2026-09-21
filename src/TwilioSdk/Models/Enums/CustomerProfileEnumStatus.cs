using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification status of the Customer-Profile resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CustomerProfileEnumStatus>))]
public sealed record CustomerProfileEnumStatus : StringEnum<CustomerProfileEnumStatus>
{
    private CustomerProfileEnumStatus(string value) : base(value)
    {
    }

    public static readonly CustomerProfileEnumStatus Draft = new("draft");

    public static readonly CustomerProfileEnumStatus PendingReview = new("pending-review");

    public static readonly CustomerProfileEnumStatus InReview = new("in-review");

    public static readonly CustomerProfileEnumStatus TwilioRejected = new("twilio-rejected");

    public static readonly CustomerProfileEnumStatus TwilioApproved = new("twilio-approved");

    public static CustomerProfileEnumStatus FromValue(string value) => FromValueCore(value);
}
