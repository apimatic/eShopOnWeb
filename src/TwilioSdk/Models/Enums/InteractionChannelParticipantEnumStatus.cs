using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelParticipantEnumStatus>))]
public sealed record InteractionChannelParticipantEnumStatus : StringEnum<InteractionChannelParticipantEnumStatus>
{
    private InteractionChannelParticipantEnumStatus(string value) : base(value)
    {
    }

    public static readonly InteractionChannelParticipantEnumStatus Closed = new("closed");

    public static readonly InteractionChannelParticipantEnumStatus Wrapup = new("wrapup");

    public static InteractionChannelParticipantEnumStatus FromValue(string value) => FromValueCore(value);
}
