using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their (single) subscription in the billing provider.
/// </summary>
public class BillingSubscriptionClaim : BillingClaim, IAggregateRoot
{
    public string PlanHandle { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingSubscriptionClaim() { }

    public BillingSubscriptionClaim(string userKey, string planHandle, string subscriptionReference, DateTimeOffset now, TimeSpan lease)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Start(userKey, subscriptionReference, now, lease);
        PlanHandle = planHandle;
    }

    public void ChangePlan(string planHandle)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PlanHandle = planHandle;
    }

    public void Complete(int providerSubscriptionId, string planHandle, DateTimeOffset now)
    {
        ChangePlan(planHandle);
        ProviderSubscriptionId = providerSubscriptionId;
        MarkCompleted(now);
    }
}
