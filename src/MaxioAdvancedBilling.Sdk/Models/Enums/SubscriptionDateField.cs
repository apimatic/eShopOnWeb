using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionDateField>))]
public sealed record SubscriptionDateField : StringEnum<SubscriptionDateField>
{
    private SubscriptionDateField(string value) : base(value)
    {
    }

    public static readonly SubscriptionDateField CurrentPeriodEndsAt = new("current_period_ends_at");

    public static readonly SubscriptionDateField CurrentPeriodStartsAt = new("current_period_starts_at");

    public static readonly SubscriptionDateField CreatedAt = new("created_at");

    public static readonly SubscriptionDateField ActivatedAt = new("activated_at");

    public static readonly SubscriptionDateField CanceledAt = new("canceled_at");

    public static readonly SubscriptionDateField ExpiresAt = new("expires_at");

    public static readonly SubscriptionDateField TrialStartedAt = new("trial_started_at");

    public static readonly SubscriptionDateField TrialEndedAt = new("trial_ended_at");

    public static readonly SubscriptionDateField UpdatedAt = new("updated_at");

    public static SubscriptionDateField FromValue(string value) => FromValueCore(value);
}
