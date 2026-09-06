using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// One customer's enrolment on a <see cref="SubscriptionPlan"/>, projected onto a provider-neutral shape.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Lifecycle state as reported by the billing system (for example <c>active</c> or <c>canceled</c>).</summary>
    public string? State { get; set; }

    /// <summary>
    /// True when the subscription is one the customer is still enrolled on. Derived by excluding the
    /// terminal states rather than by listing the live ones, so a state the billing system adds later
    /// is treated as live instead of silently disappearing from the customer's account.
    /// </summary>
    public bool IsLive { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>
    /// How the billing system collects payment for this subscription, for example <c>remittance</c>
    /// (invoice the customer) or <c>automatic</c> (charge a stored payment method).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>
    /// End of the current billing period. This is the billing system's answer to "when is the next
    /// bill" — there is no separate next-billing field on a subscription.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>When the next assessment runs. Null before the subscription is activated.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
