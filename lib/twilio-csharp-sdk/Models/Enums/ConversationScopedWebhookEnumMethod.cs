using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConversationScopedWebhookEnumMethod>))]
public sealed record ConversationScopedWebhookEnumMethod : StringEnum<ConversationScopedWebhookEnumMethod>
{
    private ConversationScopedWebhookEnumMethod(string value) : base(value)
    {
    }

    public static readonly ConversationScopedWebhookEnumMethod Get = new("get");

    public static readonly ConversationScopedWebhookEnumMethod Post = new("post");

    public static ConversationScopedWebhookEnumMethod FromValue(string value) => FromValueCore(value);
}
