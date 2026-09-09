using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WebhookStatus>))]
public sealed record WebhookStatus : StringEnum<WebhookStatus>
{
    private WebhookStatus(string value) : base(value)
    {
    }

    public static readonly WebhookStatus Successful = new("successful");

    public static readonly WebhookStatus Failed = new("failed");

    public static readonly WebhookStatus Pending = new("pending");

    public static readonly WebhookStatus Paused = new("paused");

    public static WebhookStatus FromValue(string value) => FromValueCore(value);
}
