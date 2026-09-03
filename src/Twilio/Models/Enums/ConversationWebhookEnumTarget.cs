using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The routing target of the webhook. Can be ordinary or route internally to Flex
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationWebhookEnumTarget>))]
public sealed record ConversationWebhookEnumTarget : StringEnum<ConversationWebhookEnumTarget>
{
    private ConversationWebhookEnumTarget(string value) : base(value)
    {
    }

    public static readonly ConversationWebhookEnumTarget Webhook = new("webhook");

    public static readonly ConversationWebhookEnumTarget Flex = new("flex");

    public static ConversationWebhookEnumTarget FromValue(string value) => FromValueCore(value);
}
