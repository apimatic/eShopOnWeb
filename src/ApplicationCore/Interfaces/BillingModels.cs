using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum BillingSubscriptionState
{
    Unknown,
    Pending,
    AwaitingSignup,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Canceled,
    Expired,
    Paused,
    Unpaid,
    TrialEnded,
    OnHold,
    FailedToCreate
}

public record BillingPlan(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);

public record BillingCustomer(int Id, string? Reference, string? Email, string? FirstName, string? LastName);

public record BillingSubscription(
    int Id,
    BillingSubscriptionState State,
    string? ProductHandle,
    string? ProductName,
    long PriceInCents,
    int CustomerId,
    string? CustomerReference,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? DelayedCancelAt);

public record UsageResult(long UsageId, double Quantity, string? Memo, int? UnitBalance);

public record PlanChangePreview(
    long ProratedAdjustmentInCents,
    long ChargeInCents,
    long PaymentDueInCents,
    long CreditAppliedInCents,
    DateTimeOffset EffectiveAt);
