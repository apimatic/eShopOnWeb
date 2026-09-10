using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionGroupInclude>))]
public sealed record SubscriptionGroupInclude : StringEnum<SubscriptionGroupInclude>
{
    private SubscriptionGroupInclude(string value) : base(value)
    {
    }

    public static readonly SubscriptionGroupInclude CurrentBillingAmountInCents = new("current_billing_amount_in_cents");

    public static SubscriptionGroupInclude FromValue(string value) => FromValueCore(value);
}
