using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local coordination state for an enrollment. Maxio remains the source of truth
/// for the subscription's price, state, and billing dates.
/// </summary>
public sealed class MaxioSubscriptionEnrollment
{
    private MaxioSubscriptionEnrollment()
    {
    }

    public MaxioSubscriptionEnrollment(
        string userId,
        string productHandle,
        string providerReference,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        ProviderReference = providerReference;
        LeaseOwner = leaseOwner;
        LeaseExpiresAt = leaseExpiresAt;
        Status = "pending";
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string ProviderReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public bool HasActiveLease(DateTimeOffset now) =>
        LeaseOwner is not null && LeaseExpiresAt > now;

    public void AcquireLease(string owner, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        LeaseOwner = owner;
        LeaseExpiresAt = expiresAt;
        Status = "pending";
        Touch(now);
    }

    public void Complete(int subscriptionId, string status, DateTimeOffset now)
    {
        MaxioSubscriptionId = subscriptionId;
        Status = status;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        Touch(now);
    }

    public void Reject(DateTimeOffset now)
    {
        Status = "rejected";
        LeaseOwner = null;
        LeaseExpiresAt = null;
        Touch(now);
    }

    public void MarkOutcomeUnknown(DateTimeOffset now)
    {
        Status = "outcome_unknown";
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}
