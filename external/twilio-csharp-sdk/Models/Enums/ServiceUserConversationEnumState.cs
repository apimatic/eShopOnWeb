using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The current state of this User Conversation. One of <c>inactive</c>, <c>active</c> or <c>closed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceUserConversationEnumState>))]
public sealed record ServiceUserConversationEnumState : StringEnum<ServiceUserConversationEnumState>
{
    private ServiceUserConversationEnumState(string value) : base(value)
    {
    }

    public static readonly ServiceUserConversationEnumState Inactive = new("inactive");

    public static readonly ServiceUserConversationEnumState Active = new("active");

    public static readonly ServiceUserConversationEnumState Closed = new("closed");

    public static ServiceUserConversationEnumState FromValue(string value) => FromValueCore(value);
}
