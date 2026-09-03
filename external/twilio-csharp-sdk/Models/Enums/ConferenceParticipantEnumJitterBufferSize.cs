using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceParticipantEnumJitterBufferSize>))]
public sealed record ConferenceParticipantEnumJitterBufferSize : StringEnum<ConferenceParticipantEnumJitterBufferSize>
{
    private ConferenceParticipantEnumJitterBufferSize(string value) : base(value)
    {
    }

    public static readonly ConferenceParticipantEnumJitterBufferSize Large = new("large");

    public static readonly ConferenceParticipantEnumJitterBufferSize Small = new("small");

    public static readonly ConferenceParticipantEnumJitterBufferSize Medium = new("medium");

    public static readonly ConferenceParticipantEnumJitterBufferSize Off = new("off");

    public static ConferenceParticipantEnumJitterBufferSize FromValue(string value) => FromValueCore(value);
}
