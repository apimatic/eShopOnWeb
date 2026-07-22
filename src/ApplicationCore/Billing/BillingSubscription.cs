using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A customer's subscription as reported by the billing provider.
/// </summary>
public class BillingSubscription
{
    public int Id { get; set; }

    public BillingSubscriptionState State { get; set; }

    /// <summary>
    /// The raw provider state string, preserved so an unmodelled state can still be surfaced.
    /// </summary>
    public string? ProviderState { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>
    /// Plan price for this subscription in major currency units (e.g. 299.00 for $299.00).
    /// </summary>
    public decimal PlanPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the provider will next assess (bill) this subscription.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>
    /// Outstanding balance in major currency units.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// True when a cancellation has been scheduled for the end of the current period.
    /// </summary>
    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? ScheduledCancellationAt { get; set; }

    /// <summary>
    /// Handle of a plan change that is scheduled to take effect at the next renewal, when one is pending.
    /// </summary>
    public string? PendingPlanHandle { get; set; }
}
