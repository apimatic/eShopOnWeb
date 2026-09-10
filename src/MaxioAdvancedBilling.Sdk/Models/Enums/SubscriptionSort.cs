using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionSort>))]
public sealed record SubscriptionSort : StringEnum<SubscriptionSort>
{
    private SubscriptionSort(string value) : base(value)
    {
    }

    public static readonly SubscriptionSort SignupDate = new("signup_date");

    public static readonly SubscriptionSort PeriodStart = new("period_start");

    public static readonly SubscriptionSort PeriodEnd = new("period_end");

    public static readonly SubscriptionSort NextAssessment = new("next_assessment");

    public static readonly SubscriptionSort UpdatedAt = new("updated_at");

    public static readonly SubscriptionSort CreatedAt = new("created_at");

    public static readonly SubscriptionSort TotalPayments = new("total_payments");

    public static readonly SubscriptionSort Id = new("id");

    public static readonly SubscriptionSort OpenBalance = new("open_balance");

    public static readonly SubscriptionSort ExpiresAt = new("expires_at");

    public static SubscriptionSort FromValue(string value) => FromValueCore(value);
}
