using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The of the webhook type of the configuration to be deleted
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PortingWebhookConfigurationDeleteEnumWebhookType>))]
public sealed record PortingWebhookConfigurationDeleteEnumWebhookType : StringEnum<PortingWebhookConfigurationDeleteEnumWebhookType>
{
    private PortingWebhookConfigurationDeleteEnumWebhookType(string value) : base(value)
    {
    }

    public static readonly PortingWebhookConfigurationDeleteEnumWebhookType PortIn = new("PORT_IN");

    public static readonly PortingWebhookConfigurationDeleteEnumWebhookType PortOut = new("PORT_OUT");

    public static PortingWebhookConfigurationDeleteEnumWebhookType FromValue(string value) =>
        FromValueCore(value);
}
