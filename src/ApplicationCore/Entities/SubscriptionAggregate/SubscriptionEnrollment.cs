using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A user's enrollment in one subscription plan. The row is the claim that stops a double-submit from
/// creating two subscriptions: it is inserted (primary key = user + plan) before the subscription is created,
/// and it stores the reference sent with the create so an unknown outcome can be looked up later.
/// </summary>
public class SubscriptionEnrollment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }
    #pragma warning restore CS8618

    public SubscriptionEnrollment(string userId, string planHandle, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Id = KeyFor(userId, planHandle);
        UserId = userId;
        PlanHandle = planHandle;
        BillingReference = $"eshop-sub-{Guid.NewGuid():N}";
        Status = EnrollmentStatus.Pending;
        ClaimedAt = claimedAt;
        ClaimToken = Guid.NewGuid();
    }

    public string Id { get; private set; }
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The subscription reference sent to the billing system with the create call.</summary>
    public string BillingReference { get; private set; }

    public EnrollmentStatus Status { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Concurrency token: changes whenever a request takes over a stale claim.</summary>
    public Guid ClaimToken { get; private set; }

    public static string KeyFor(string userId, string planHandle) => $"{userId}:{planHandle}";

    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == EnrollmentStatus.Pending && now - ClaimedAt > staleAfter;

    public void Renew(DateTimeOffset now)
    {
        ClaimedAt = now;
        ClaimToken = Guid.NewGuid();
    }

    public void MarkCompleted(int billingSubscriptionId, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        BillingSubscriptionId = billingSubscriptionId;
        Status = EnrollmentStatus.Completed;
        CompletedAt = now;
    }
}
