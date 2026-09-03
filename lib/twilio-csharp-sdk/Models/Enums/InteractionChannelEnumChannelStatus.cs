using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of this channel.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionChannelEnumChannelStatus>))]
public sealed record InteractionChannelEnumChannelStatus : StringEnum<InteractionChannelEnumChannelStatus>
{
    private InteractionChannelEnumChannelStatus(string value) : base(value)
    {
    }

    public static readonly InteractionChannelEnumChannelStatus Setup = new("setup");

    public static readonly InteractionChannelEnumChannelStatus Active = new("active");

    public static readonly InteractionChannelEnumChannelStatus Failed = new("failed");

    public static readonly InteractionChannelEnumChannelStatus Closed = new("closed");

    public static readonly InteractionChannelEnumChannelStatus Inactive = new("inactive");

    public static readonly InteractionChannelEnumChannelStatus Pause = new("pause");

    public static readonly InteractionChannelEnumChannelStatus Transfer = new("transfer");

    public static InteractionChannelEnumChannelStatus FromValue(string value) => FromValueCore(value);
}
