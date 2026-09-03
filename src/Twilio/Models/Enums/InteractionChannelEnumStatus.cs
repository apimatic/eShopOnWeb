using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of this channel.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionChannelEnumStatus>))]
public sealed record InteractionChannelEnumStatus : StringEnum<InteractionChannelEnumStatus>
{
    private InteractionChannelEnumStatus(string value) : base(value)
    {
    }

    public static readonly InteractionChannelEnumStatus Closed = new("closed");

    public static readonly InteractionChannelEnumStatus Wrapup = new("wrapup");

    public static InteractionChannelEnumStatus FromValue(string value) => FromValueCore(value);
}
