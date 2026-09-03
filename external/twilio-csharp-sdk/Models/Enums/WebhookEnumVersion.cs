using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The webhook version. Default value is <c>v2</c> which includes all the latest fields. Version <c>v1</c> is legacy and may be removed in the future.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WebhookEnumVersion>))]
public sealed record WebhookEnumVersion : StringEnum<WebhookEnumVersion>
{
    private WebhookEnumVersion(string value) : base(value)
    {
    }

    public static readonly WebhookEnumVersion V1 = new("v1");

    public static readonly WebhookEnumVersion V2 = new("v2");

    public static WebhookEnumVersion FromValue(string value) => FromValueCore(value);
}
