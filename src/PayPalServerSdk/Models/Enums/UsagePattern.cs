using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Expected business/pricing model for the billing agreement., Expected business/charge model for the billing agreement.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UsagePattern>))]
public sealed record UsagePattern : OpenStringEnum<UsagePattern>
{
    private UsagePattern(string value) : base(value)
    {
    }

    /// <summary>
    /// On-demand instant payments – non-recurring, pre-paid, variable amount, variable frequency.
    /// </summary>
    public static readonly UsagePattern Immediate = new("IMMEDIATE");

    /// <summary>
    /// Pay after use, non-recurring post-paid, variable amount, irregular frequency.
    /// </summary>
    public static readonly UsagePattern Deferred = new("DEFERRED");

    /// <summary>
    /// Pay upfront fixed or variable amount on a fixed date before the goods/service is delivered.
    /// </summary>
    public static readonly UsagePattern RecurringPrepaid = new("RECURRING_PREPAID");

    /// <summary>
    /// Pay on a fixed date based on usage or consumption after the goods/service is delivered.
    /// </summary>
    public static readonly UsagePattern RecurringPostpaid = new("RECURRING_POSTPAID");

    /// <summary>
    /// Charge payer when the set amount is reached or monthly billing cycle, whichever comes first, before the goods/service is delivered.
    /// </summary>
    public static readonly UsagePattern ThresholdPrepaid = new("THRESHOLD_PREPAID");

    /// <summary>
    /// Charge payer when the set amount is reached or monthly billing cycle, whichever comes first, after the goods/service is delivered.
    /// </summary>
    public static readonly UsagePattern ThresholdPostpaid = new("THRESHOLD_POSTPAID");

    /// <summary>
    /// Subscription plan where the "amount due" and the "billing frequency" are fixed, and there is no defined duration with the payment due before the good/service is delivered.
    /// </summary>
    public static readonly UsagePattern SubscriptionPrepaid = new("SUBSCRIPTION_PREPAID");

    /// <summary>
    /// Subscription plan where the "amount due" and the "billing frequency" are fixed, and there is no defined duration with the payment due after the goods/services are delivered.
    /// </summary>
    public static readonly UsagePattern SubscriptionPostpaid = new("SUBSCRIPTION_POSTPAID");

    /// <summary>
    /// Unscheduled card on file plan where the merchant can bill buyer upfront based on an agreed logic, but "amount due" and "frequency" can vary. Inclusive of automatic reload plans.
    /// </summary>
    public static readonly UsagePattern UnscheduledPrepaid = new("UNSCHEDULED_PREPAID");

    /// <summary>
    /// Unscheduled card on file plan where the merchant can bill buyer based on an agreed logic, but "amount due" and "frequency" can vary. Inclusive of automatic reload plans.
    /// </summary>
    public static readonly UsagePattern UnscheduledPostpaid = new("UNSCHEDULED_POSTPAID");

    /// <summary>
    /// Merchant-managed installment plan when the "amount" to be paid and the "billing frequency" are fixed, but there is a defined number of payments with the payment due before the good/service is delivered.
    /// </summary>
    public static readonly UsagePattern InstallmentPrepaid = new("INSTALLMENT_PREPAID");

    /// <summary>
    /// Merchant-managed installment plan when the "amount" to be paid and the "billing frequency" are fixed, but there is a defined number of payments with the payment due after the goods/services are delivered.
    /// </summary>
    public static readonly UsagePattern InstallmentPostpaid = new("INSTALLMENT_POSTPAID");

    public TResult Match<TResult>(Func<TResult> onImmediate,
        Func<TResult> onDeferred,
        Func<TResult> onRecurringPrepaid,
        Func<TResult> onRecurringPostpaid,
        Func<TResult> onThresholdPrepaid,
        Func<TResult> onThresholdPostpaid,
        Func<TResult> onSubscriptionPrepaid,
        Func<TResult> onSubscriptionPostpaid,
        Func<TResult> onUnscheduledPrepaid,
        Func<TResult> onUnscheduledPostpaid,
        Func<TResult> onInstallmentPrepaid,
        Func<TResult> onInstallmentPostpaid,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Immediate => onImmediate(),
            _ when this == Deferred => onDeferred(),
            _ when this == RecurringPrepaid => onRecurringPrepaid(),
            _ when this == RecurringPostpaid => onRecurringPostpaid(),
            _ when this == ThresholdPrepaid => onThresholdPrepaid(),
            _ when this == ThresholdPostpaid => onThresholdPostpaid(),
            _ when this == SubscriptionPrepaid => onSubscriptionPrepaid(),
            _ when this == SubscriptionPostpaid => onSubscriptionPostpaid(),
            _ when this == UnscheduledPrepaid => onUnscheduledPrepaid(),
            _ when this == UnscheduledPostpaid => onUnscheduledPostpaid(),
            _ when this == InstallmentPrepaid => onInstallmentPrepaid(),
            _ when this == InstallmentPostpaid => onInstallmentPostpaid(),
            _ => otherwise(Value)
        };

    public void Match(Action onImmediate,
        Action onDeferred,
        Action onRecurringPrepaid,
        Action onRecurringPostpaid,
        Action onThresholdPrepaid,
        Action onThresholdPostpaid,
        Action onSubscriptionPrepaid,
        Action onSubscriptionPostpaid,
        Action onUnscheduledPrepaid,
        Action onUnscheduledPostpaid,
        Action onInstallmentPrepaid,
        Action onInstallmentPostpaid,
        Action<string> otherwise)
    {
        if (this == Immediate) onImmediate();
        else if (this == Deferred) onDeferred();
        else if (this == RecurringPrepaid) onRecurringPrepaid();
        else if (this == RecurringPostpaid) onRecurringPostpaid();
        else if (this == ThresholdPrepaid) onThresholdPrepaid();
        else if (this == ThresholdPostpaid) onThresholdPostpaid();
        else if (this == SubscriptionPrepaid) onSubscriptionPrepaid();
        else if (this == SubscriptionPostpaid) onSubscriptionPostpaid();
        else if (this == UnscheduledPrepaid) onUnscheduledPrepaid();
        else if (this == UnscheduledPostpaid) onUnscheduledPostpaid();
        else if (this == InstallmentPrepaid) onInstallmentPrepaid();
        else if (this == InstallmentPostpaid) onInstallmentPostpaid();
        else otherwise(Value);
    }
}
