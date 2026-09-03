using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The current state of this User Conversation. One of <c>inactive</c>, <c>active</c> or <c>closed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ParticipantConversationEnumState>))]
public sealed record ParticipantConversationEnumState : StringEnum<ParticipantConversationEnumState>
{
    private ParticipantConversationEnumState(string value) : base(value)
    {
    }

    public static readonly ParticipantConversationEnumState Inactive = new("inactive");

    public static readonly ParticipantConversationEnumState Active = new("active");

    public static readonly ParticipantConversationEnumState Closed = new("closed");

    public static ParticipantConversationEnumState FromValue(string value) => FromValueCore(value);
}
