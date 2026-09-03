using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Current state of this conversation. Can be either <c>initializing</c>, <c>active</c>, <c>inactive</c> or <c>closed</c> and defaults to <c>active</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationWithParticipantsEnumState>))]
public sealed record ConversationWithParticipantsEnumState : StringEnum<ConversationWithParticipantsEnumState>
{
    private ConversationWithParticipantsEnumState(string value) : base(value)
    {
    }

    public static readonly ConversationWithParticipantsEnumState Initializing = new("initializing");

    public static readonly ConversationWithParticipantsEnumState Inactive = new("inactive");

    public static readonly ConversationWithParticipantsEnumState Active = new("active");

    public static readonly ConversationWithParticipantsEnumState Closed = new("closed");

    public static ConversationWithParticipantsEnumState FromValue(string value) => FromValueCore(value);
}
