using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// One subscribe attempt per user. The primary key is the customer reference, so inserting the row is
/// the claim that stops a double-submit (or two racing requests) from creating two subscriptions.
/// <see cref="SubscriptionReference"/> is generated once, stored here before the provider is called,
/// and sent to the provider as the subscription's own reference so an unknown outcome can be looked up.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }
    #pragma warning restore CS8618

    public SubscriptionClaim(string customerReference, string subscriptionReference, string planHandle, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        PlanHandle = planHandle;
        Status = BillingClaimStatus.Pending;
        ClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
        Version = Guid.NewGuid();
    }

    /// <summary>Primary key: the owning user's customer reference (one subscribe claim per user).</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The reference sent to the provider with the subscription; the unknown-outcome search key.</summary>
    public string SubscriptionReference { get; private set; }

    public string PlanHandle { get; private set; }

    public int? ProviderSubscriptionId { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concurrency token: changes on every mutation so two callers cannot both take over a stale claim.</summary>
    public Guid Version { get; private set; }

    /// <summary>Records the provider's subscription for this claim (the one created, or an existing one found).</summary>
    public void Complete(int providerSubscriptionId, string planHandle, string? subscriptionReference, DateTimeOffset at)
    {
        Guard.Against.NegativeOrZero(providerSubscriptionId, nameof(providerSubscriptionId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        ProviderSubscriptionId = providerSubscriptionId;
        PlanHandle = planHandle;
        if (!string.IsNullOrWhiteSpace(subscriptionReference))
        {
            SubscriptionReference = subscriptionReference;
        }
        Status = BillingClaimStatus.Completed;
        UpdatedAt = at;
        Version = Guid.NewGuid();
    }
}
