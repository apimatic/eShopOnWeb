using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A buyer's enrollment in one subscription plan. The primary key is the deterministic subscription
/// reference (buyer + plan), which is also sent to the billing provider, so a double-submit is refused
/// by the store before a second subscription can be created.
/// </summary>
public class SubscriptionEnrollment : BillingClaim, IAggregateRoot
{
    public string Reference { get; private set; }

    public string BuyerId { get; private set; }

    public string PlanHandle { get; private set; }

    public int? ProviderSubscriptionId { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string reference, string buyerId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Reference = reference;
        BuyerId = buyerId;
        PlanHandle = planHandle;
        StartClaim(now);
    }

    public void MarkCreated(int providerSubscriptionId, DateTimeOffset now)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        Status = BillingClaimStatus.Completed;
        UpdatedAt = now;
    }
}
