using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionInclude>))]
public sealed record SubscriptionInclude : StringEnum<SubscriptionInclude>
{
    private SubscriptionInclude(string value) : base(value)
    {
    }

    public static readonly SubscriptionInclude Coupons = new("coupons");

    public static readonly SubscriptionInclude SelfServicePageToken = new("self_service_page_token");

    public static SubscriptionInclude FromValue(string value) => FromValueCore(value);
}
