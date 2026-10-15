using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A user's claim on subscribing to one plan. The primary key is (user, plan), so inserting the
/// row is what refuses a second concurrent subscribe (a double-click) before it reaches the provider.
/// <see cref="Reference"/> is sent to the provider with the subscription and is how an
/// unknown outcome is settled afterwards.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    public string UserKey { get; private set; }
    public string PlanHandle { get; private set; }
    public string Reference { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concurrency token: changes on every update so two writers cannot both take over a claim.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }

    public SubscriptionClaim(string userKey, string planHandle, string reference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        UserKey = userKey;
        PlanHandle = planHandle;
        Reference = reference;
        Status = BillingClaimStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>A pending claim whose holder has gone quiet for longer than the lease may be taken over.</summary>
    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan lease) =>
        Status == BillingClaimStatus.Pending && now - UpdatedAt > lease;

    public void Complete(int billingSubscriptionId, DateTimeOffset now)
    {
        BillingSubscriptionId = billingSubscriptionId;
        Status = BillingClaimStatus.Completed;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    /// <summary>Takes the claim over to retry the same write: the reference is kept so the provider can still be searched by it.</summary>
    public void Resume(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    /// <summary>Re-opens a completed claim for a new subscription (the earlier one ended), under a new reference.</summary>
    public void Restart(string newReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(newReference, nameof(newReference));
        Reference = newReference;
        BillingSubscriptionId = null;
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
