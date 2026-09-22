using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelEnumUpdateChannelStatus>))]
public sealed record InteractionChannelEnumUpdateChannelStatus : StringEnum<InteractionChannelEnumUpdateChannelStatus>
{
    private InteractionChannelEnumUpdateChannelStatus(string value) : base(value)
    {
    }

    public static readonly InteractionChannelEnumUpdateChannelStatus Closed = new("closed");

    public static readonly InteractionChannelEnumUpdateChannelStatus Inactive = new("inactive");

    public static InteractionChannelEnumUpdateChannelStatus FromValue(string value) => FromValueCore(value);
}
