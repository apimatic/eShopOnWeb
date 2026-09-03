using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Notification Level of this User Conversation. One of <c>default</c> or <c>muted</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceUserConversationEnumNotificationLevel>))]
public sealed record ServiceUserConversationEnumNotificationLevel : StringEnum<ServiceUserConversationEnumNotificationLevel>
{
    private ServiceUserConversationEnumNotificationLevel(string value) : base(value)
    {
    }

    public static readonly ServiceUserConversationEnumNotificationLevel Default = new("default");

    public static readonly ServiceUserConversationEnumNotificationLevel Muted = new("muted");

    public static ServiceUserConversationEnumNotificationLevel FromValue(string value) => FromValueCore(value);
}
