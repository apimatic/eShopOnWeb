using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The current state of this User Conversation. One of <c>inactive</c>, <c>active</c> or <c>closed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceParticipantConversationEnumState>))]
public sealed record ServiceParticipantConversationEnumState : StringEnum<ServiceParticipantConversationEnumState>
{
    private ServiceParticipantConversationEnumState(string value) : base(value)
    {
    }

    public static readonly ServiceParticipantConversationEnumState Inactive = new("inactive");

    public static readonly ServiceParticipantConversationEnumState Active = new("active");

    public static readonly ServiceParticipantConversationEnumState Closed = new("closed");

    public static ServiceParticipantConversationEnumState FromValue(string value) => FromValueCore(value);
}
