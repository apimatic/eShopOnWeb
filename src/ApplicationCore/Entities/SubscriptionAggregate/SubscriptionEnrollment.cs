using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A user's enrollment in a subscription plan at the billing provider.
/// The primary key is (user name, plan handle): it is inserted before the provider is called, so a double-click
/// or a concurrent retry for the same plan is refused by the store instead of reaching the provider twice.
/// </summary>
public class SubscriptionEnrollment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userName, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        UserName = userName;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = BillingClaimStatus.Pending;
        Version = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserName { get; private set; }

    public string PlanHandle { get; private set; }

    /// <summary>The reference sent with the create call; the unknown-outcome path finds the subscription by it.</summary>
    public string SubscriptionReference { get; private set; }

    public int? ProviderCustomerId { get; private set; }

    public int? ProviderSubscriptionId { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    /// <summary>Optimistic-concurrency token: a takeover of a stale claim succeeds for one caller only.</summary>
    public Guid Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void AttachCustomer(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        Touch(now);
    }

    public void Complete(int providerSubscriptionId, string? subscriptionReference, DateTimeOffset now)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        if (!string.IsNullOrWhiteSpace(subscriptionReference))
        {
            SubscriptionReference = subscriptionReference;
        }
        Status = BillingClaimStatus.Completed;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version = Guid.NewGuid();
        UpdatedAt = now;
    }
}
