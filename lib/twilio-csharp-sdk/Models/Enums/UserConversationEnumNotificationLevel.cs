using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Notification Level of this User Conversation. One of <c>default</c> or <c>muted</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UserConversationEnumNotificationLevel>))]
public sealed record UserConversationEnumNotificationLevel : StringEnum<UserConversationEnumNotificationLevel>
{
    private UserConversationEnumNotificationLevel(string value) : base(value)
    {
    }

    public static readonly UserConversationEnumNotificationLevel Default = new("default");

    public static readonly UserConversationEnumNotificationLevel Muted = new("muted");

    public static UserConversationEnumNotificationLevel FromValue(string value) => FromValueCore(value);
}
