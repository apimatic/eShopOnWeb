using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The target of this webhook: <c>webhook</c>, <c>studio</c>, <c>trigger</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceConversationScopedWebhookEnumTarget>))]
public sealed record ServiceConversationScopedWebhookEnumTarget : StringEnum<ServiceConversationScopedWebhookEnumTarget>
{
    private ServiceConversationScopedWebhookEnumTarget(string value) : base(value)
    {
    }

    public static readonly ServiceConversationScopedWebhookEnumTarget Webhook = new("webhook");

    public static readonly ServiceConversationScopedWebhookEnumTarget Trigger = new("trigger");

    public static readonly ServiceConversationScopedWebhookEnumTarget Studio = new("studio");

    public static ServiceConversationScopedWebhookEnumTarget FromValue(string value) => FromValueCore(value);
}
