using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionListInclude>))]
public sealed record SubscriptionListInclude : StringEnum<SubscriptionListInclude>
{
    private SubscriptionListInclude(string value) : base(value)
    {
    }

    public static readonly SubscriptionListInclude SelfServicePageToken = new("self_service_page_token");

    public static SubscriptionListInclude FromValue(string value) => FromValueCore(value);
}
