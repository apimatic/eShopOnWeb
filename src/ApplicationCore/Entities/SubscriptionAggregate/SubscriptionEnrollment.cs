using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Records that an eShop user is enrolled in a plan in the billing system.
/// The primary key is the deterministic subscription reference sent to the provider, so a
/// double-submit for the same user and plan is refused by the store before it reaches the provider.
/// </summary>
public class SubscriptionEnrollment : IBillingClaim, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string subscriptionReference, string customerReference, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        SubscriptionReference = subscriptionReference;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        Status = BillingClaimStatus.Pending;
        ClaimedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Builds the reference that identifies one user's enrollment in one plan, both locally and at the provider.
    /// </summary>
    public static string BuildReference(string customerReference, string planHandle) =>
        $"eshop:{customerReference}:{planHandle}";

    public string SubscriptionReference { get; private set; }
    public string CustomerReference { get; private set; }
    public string PlanHandle { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public int? ProviderId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        ClaimedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Complete(int providerId, DateTimeOffset now)
    {
        Status = BillingClaimStatus.Completed;
        ProviderId = providerId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
