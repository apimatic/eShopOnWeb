using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Current state of this conversation. Can be either <c>initializing</c>, <c>active</c>, <c>inactive</c> or <c>closed</c> and defaults to <c>active</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceConversationWithParticipantsEnumState>))]
public sealed record ServiceConversationWithParticipantsEnumState : StringEnum<ServiceConversationWithParticipantsEnumState>
{
    private ServiceConversationWithParticipantsEnumState(string value) : base(value)
    {
    }

    public static readonly ServiceConversationWithParticipantsEnumState Initializing = new("initializing");

    public static readonly ServiceConversationWithParticipantsEnumState Inactive = new("inactive");

    public static readonly ServiceConversationWithParticipantsEnumState Active = new("active");

    public static readonly ServiceConversationWithParticipantsEnumState Closed = new("closed");

    public static ServiceConversationWithParticipantsEnumState FromValue(string value) => FromValueCore(value);
}
