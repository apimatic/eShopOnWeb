using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Participant type. Can be: <c>agent</c>, <c>customer</c>, <c>supervisor</c>, <c>external</c>, <c>unknown</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionChannelParticipantEnumType>))]
public sealed record InteractionChannelParticipantEnumType : StringEnum<InteractionChannelParticipantEnumType>
{
    private InteractionChannelParticipantEnumType(string value) : base(value)
    {
    }

    public static readonly InteractionChannelParticipantEnumType Supervisor = new("supervisor");

    public static readonly InteractionChannelParticipantEnumType Customer = new("customer");

    public static readonly InteractionChannelParticipantEnumType External = new("external");

    public static readonly InteractionChannelParticipantEnumType Agent = new("agent");

    public static readonly InteractionChannelParticipantEnumType Unknown = new("unknown");

    public static InteractionChannelParticipantEnumType FromValue(string value) => FromValueCore(value);
}
