using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The committed outcome of a plan change.</summary>
/// <param name="AppliedPaymentDue">
/// The amount actually charged, in major currency units. Zero for a change deferred to renewal.
/// </param>
public sealed record PlanChangeResult(
    BillingSubscription Subscription,
    string PreviousPlanHandle,
    string NewPlanHandle,
    PlanChangeTiming Timing,
    decimal AppliedPaymentDue,
    DateTimeOffset? EffectiveAt);
