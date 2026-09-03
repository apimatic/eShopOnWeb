using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The routing target of the webhook. Can be ordinary or route internally to Flex
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConfigurationWebhookEnumTarget>))]
public sealed record ConfigurationWebhookEnumTarget : StringEnum<ConfigurationWebhookEnumTarget>
{
    private ConfigurationWebhookEnumTarget(string value) : base(value)
    {
    }

    public static readonly ConfigurationWebhookEnumTarget Webhook = new("webhook");

    public static readonly ConfigurationWebhookEnumTarget Flex = new("flex");

    public static ConfigurationWebhookEnumTarget FromValue(string value) => FromValueCore(value);
}
