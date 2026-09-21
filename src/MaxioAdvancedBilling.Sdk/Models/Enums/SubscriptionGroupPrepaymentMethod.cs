using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionGroupPrepaymentMethod>))]
public sealed record SubscriptionGroupPrepaymentMethod : StringEnum<SubscriptionGroupPrepaymentMethod>
{
    private SubscriptionGroupPrepaymentMethod(string value) : base(value)
    {
    }

    public static readonly SubscriptionGroupPrepaymentMethod Check = new("check");

    public static readonly SubscriptionGroupPrepaymentMethod Cash = new("cash");

    public static readonly SubscriptionGroupPrepaymentMethod MoneyOrder = new("money_order");

    public static readonly SubscriptionGroupPrepaymentMethod Ach = new("ach");

    public static readonly SubscriptionGroupPrepaymentMethod PaypalAccount = new("paypal_account");

    public static readonly SubscriptionGroupPrepaymentMethod Other = new("other");

    public static SubscriptionGroupPrepaymentMethod FromValue(string value) => FromValueCore(value);
}
