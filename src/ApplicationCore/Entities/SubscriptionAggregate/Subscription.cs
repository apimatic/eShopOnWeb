using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Read model for a subscription's current state, projected from the billing provider.
/// eShopOnWeb keeps no local subscription table: the userId &lt;-&gt; subscription mapping is
/// stateless, resolved on demand from the idempotent customer reference (the user's email).
/// </summary>
public class Subscription
{
    public Subscription(
        int id,
        string customerReference,
        string planHandle,
        string planName,
        decimal price,
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEndsAt,
        bool cancelAtEndOfPeriod)
    {
        Id = id;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        Price = price;
        Status = status;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    public int Id { get; }
    public string CustomerReference { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }
    public decimal Price { get; }
    public SubscriptionStatus Status { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public bool CancelAtEndOfPeriod { get; }
}
