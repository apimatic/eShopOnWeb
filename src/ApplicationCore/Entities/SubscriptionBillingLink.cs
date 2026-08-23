using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum SubscriptionBillingStatus
{
    Pending,
    Confirmed,
    Failed,
    Unknown
}

public sealed class SubscriptionBillingLink : BaseEntity, IAggregateRoot
{
    private SubscriptionBillingLink()
    {
    }

    public SubscriptionBillingLink(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        string leaseToken,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        LeaseToken = leaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionBillingStatus Status { get; private set; } = SubscriptionBillingStatus.Pending;
    public string LeaseToken { get; private set; } = string.Empty;
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string ConcurrencyToken { get; private set; } = string.Empty;

    public bool LeaseIsOwnedBy(string token) =>
        string.Equals(LeaseToken, token, StringComparison.Ordinal);

    public void Claim(string leaseToken, DateTimeOffset now, DateTimeOffset leaseExpiresAt)
    {
        LeaseToken = leaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        Status = SubscriptionBillingStatus.Pending;
        Touch(now);
    }

    public void Confirm(int customerId, int subscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionBillingStatus.Confirmed;
        LeaseExpiresAt = now;
        Touch(now);
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Status = SubscriptionBillingStatus.Failed;
        LeaseExpiresAt = now;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionBillingStatus.Unknown;
        LeaseExpiresAt = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }
}
