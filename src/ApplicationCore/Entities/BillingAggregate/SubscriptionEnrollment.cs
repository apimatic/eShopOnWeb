using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A user's enrollment in a recurring plan at the billing provider. Keyed by the user id,
/// so a shopper holds at most one enrollment in flight (or live) at a time; inserting the
/// row is the claim that stops a double-submit from creating two subscriptions.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    public string UserId { get; private set; }

    /// <summary>The reference sent to the provider with the create call; used to find the
    /// subscription again when the create's outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }

    public string PlanHandle { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public static SubscriptionEnrollment Claim(string userId, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        return new SubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = subscriptionReference,
            Status = BillingClaimStatus.Pending,
            ClaimedAt = now,
            ConcurrencyStamp = Guid.NewGuid()
        };
    }

    public bool IsCompleted => Status == BillingClaimStatus.Completed && BillingSubscriptionId.HasValue;

    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan lease) =>
        Status == BillingClaimStatus.Pending && now - ClaimedAt > lease;

    /// <summary>Records the provider subscription this enrollment resolved to (created by us or
    /// already present at the provider).</summary>
    public void Complete(int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        BillingSubscriptionId = billingSubscriptionId;
        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            PlanHandle = planHandle;
        }
        Status = BillingClaimStatus.Completed;
        CompletedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
