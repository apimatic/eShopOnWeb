using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The current state of this User Conversation. One of <c>inactive</c>, <c>active</c> or <c>closed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UserConversationEnumState>))]
public sealed record UserConversationEnumState : StringEnum<UserConversationEnumState>
{
    private UserConversationEnumState(string value) : base(value)
    {
    }

    public static readonly UserConversationEnumState Inactive = new("inactive");

    public static readonly UserConversationEnumState Active = new("active");

    public static readonly UserConversationEnumState Closed = new("closed");

    public static UserConversationEnumState FromValue(string value) => FromValueCore(value);
}
