using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the sender.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChannelsSenderEnumStatus>))]
public sealed record ChannelsSenderEnumStatus : StringEnum<ChannelsSenderEnumStatus>
{
    private ChannelsSenderEnumStatus(string value) : base(value)
    {
    }

    public static readonly ChannelsSenderEnumStatus Creating = new("CREATING");

    public static readonly ChannelsSenderEnumStatus Online = new("ONLINE");

    public static readonly ChannelsSenderEnumStatus Offline = new("OFFLINE");

    public static readonly ChannelsSenderEnumStatus PendingVerification = new("PENDING_VERIFICATION");

    public static readonly ChannelsSenderEnumStatus Verifying = new("VERIFYING");

    public static readonly ChannelsSenderEnumStatus OnlineUpdating = new("ONLINE:UPDATING");

    public static readonly ChannelsSenderEnumStatus TwilioReview = new("TWILIO_REVIEW");

    public static readonly ChannelsSenderEnumStatus Draft = new("DRAFT");

    public static readonly ChannelsSenderEnumStatus Stubbed = new("STUBBED");

    public static ChannelsSenderEnumStatus FromValue(string value) => FromValueCore(value);
}
