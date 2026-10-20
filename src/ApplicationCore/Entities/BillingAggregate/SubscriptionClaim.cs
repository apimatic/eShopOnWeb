using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Records a shopper's subscription to one plan at the billing provider.
/// </summary>
public class SubscriptionClaim : BillingClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }

    public SubscriptionClaim(string reference, string buyerId, string planHandle, DateTimeOffset now)
        : base(reference, buyerId, now)
    {
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
    }

    public string PlanHandle { get; private set; }

    /// <summary>The provider's id for the subscription, once known.</summary>
    public int? BillingSubscriptionId { get; private set; }

    public void Complete(int billingSubscriptionId, DateTimeOffset now)
    {
        BillingSubscriptionId = billingSubscriptionId;
        MarkCompleted(now);
    }
}
