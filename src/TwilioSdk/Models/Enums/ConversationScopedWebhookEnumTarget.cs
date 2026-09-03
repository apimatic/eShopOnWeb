using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The target of this webhook: <c>webhook</c>, <c>studio</c>, <c>trigger</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationScopedWebhookEnumTarget>))]
public sealed record ConversationScopedWebhookEnumTarget : StringEnum<ConversationScopedWebhookEnumTarget>
{
    private ConversationScopedWebhookEnumTarget(string value) : base(value)
    {
    }

    public static readonly ConversationScopedWebhookEnumTarget Webhook = new("webhook");

    public static readonly ConversationScopedWebhookEnumTarget Trigger = new("trigger");

    public static readonly ConversationScopedWebhookEnumTarget Studio = new("studio");

    public static ConversationScopedWebhookEnumTarget FromValue(string value) => FromValueCore(value);
}
