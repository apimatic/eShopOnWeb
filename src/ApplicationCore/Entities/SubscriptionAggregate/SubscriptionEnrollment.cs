using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A durable enrollment reservation. Maxio remains the source of truth for billing state.
/// </summary>
public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string maxioSubscriptionReference,
        string provisioningOwner, DateTimeOffset leaseExpiresAtUtc)
    {
        UserId = userId;
        ProductHandle = productHandle;
        MaxioSubscriptionReference = maxioSubscriptionReference;
        ProvisioningOwner = provisioningOwner;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        ProvisioningState = SubscriptionProvisioningState.Provisioning;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string MaxioSubscriptionReference { get; private set; } = string.Empty;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public string? ProductName { get; private set; }
    public long? PriceInCents { get; private set; }
    public int? BillingInterval { get; private set; }
    public string? BillingIntervalUnit { get; private set; }
    public string? SubscriptionState { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public SubscriptionProvisioningState ProvisioningState { get; private set; }
    public string? ProvisioningOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void AcquireLease(string owner, DateTimeOffset leaseExpiresAtUtc)
    {
        ProvisioningOwner = owner;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        ProvisioningState = SubscriptionProvisioningState.Provisioning;
        Touch();
    }

    public void MarkProvisioned(long customerId, long subscriptionId, string productName, long priceInCents,
        int interval, string intervalUnit, string state, DateTimeOffset? nextBillingAt)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        ProductName = productName;
        PriceInCents = priceInCents;
        BillingInterval = interval;
        BillingIntervalUnit = intervalUnit;
        SubscriptionState = state;
        NextBillingAt = nextBillingAt;
        ProvisioningState = SubscriptionProvisioningState.Provisioned;
        ProvisioningOwner = null;
        LeaseExpiresAtUtc = null;
        Touch();
    }

    public void MarkFailed()
    {
        ProvisioningState = SubscriptionProvisioningState.Failed;
        ProvisioningOwner = null;
        LeaseExpiresAtUtc = null;
        Touch();
    }

    private void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionProvisioningState
{
    Provisioning,
    Provisioned,
    Failed
}
