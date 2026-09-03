using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ServiceConversationScopedWebhookEnumMethod>))]
public sealed record ServiceConversationScopedWebhookEnumMethod : StringEnum<ServiceConversationScopedWebhookEnumMethod>
{
    private ServiceConversationScopedWebhookEnumMethod(string value) : base(value)
    {
    }

    public static readonly ServiceConversationScopedWebhookEnumMethod Get = new("get");

    public static readonly ServiceConversationScopedWebhookEnumMethod Post = new("post");

    public static ServiceConversationScopedWebhookEnumMethod FromValue(string value) => FromValueCore(value);
}
