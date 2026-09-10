using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WebhookOrder>))]
public sealed record WebhookOrder : StringEnum<WebhookOrder>
{
    private WebhookOrder(string value) : base(value)
    {
    }

    public static readonly WebhookOrder NewestFirst = new("newest_first");

    public static readonly WebhookOrder OldestFirst = new("oldest_first");

    public static WebhookOrder FromValue(string value) => FromValueCore(value);
}
