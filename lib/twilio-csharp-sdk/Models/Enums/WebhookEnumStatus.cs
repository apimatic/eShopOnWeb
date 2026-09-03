using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The webhook status. Default value is <c>enabled</c>. One of: <c>enabled</c> or <c>disabled</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WebhookEnumStatus>))]
public sealed record WebhookEnumStatus : StringEnum<WebhookEnumStatus>
{
    private WebhookEnumStatus(string value) : base(value)
    {
    }

    public static readonly WebhookEnumStatus Enabled = new("enabled");

    public static readonly WebhookEnumStatus Disabled = new("disabled");

    public static WebhookEnumStatus FromValue(string value) => FromValueCore(value);
}
