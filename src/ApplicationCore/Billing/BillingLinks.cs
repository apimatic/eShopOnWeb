using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class MaxioCustomerLink : BaseEntity
{
    private MaxioCustomerLink() { }

    public MaxioCustomerLink(string userId, string customerReference, string leaseId, DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        CustomerReference = customerReference;
        LeaseId = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        Status = BillingLinkStatus.Pending;
        Version = Guid.NewGuid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public BillingLinkStatus Status { get; private set; }
    public string? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public Guid Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? LastError { get; private set; }

    public void Acquire(string leaseId, DateTimeOffset leaseExpiresAt)
    {
        Status = BillingLinkStatus.Pending;
        LeaseId = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        LastError = null;
        Touch();
    }

    public void Complete()
    {
        Status = BillingLinkStatus.Completed;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastError = null;
        Touch();
    }

    public void Fail(bool retryable, string safeError)
    {
        Status = retryable ? BillingLinkStatus.RetryableFailure : BillingLinkStatus.TerminalFailure;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastError = safeError;
        Touch();
    }

    private void Touch()
    {
        Version = Guid.NewGuid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class MaxioSubscriptionLink : BaseEntity
{
    private MaxioSubscriptionLink() { }

    public MaxioSubscriptionLink(
        string userId,
        string productHandle,
        string subscriptionReference,
        string leaseId,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        LeaseId = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        Status = BillingLinkStatus.Pending;
        Version = Guid.NewGuid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public BillingLinkStatus Status { get; private set; }
    public string? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public Guid Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? LastError { get; private set; }
    public string? ProductName { get; private set; }
    public long? PriceInCents { get; private set; }
    public string? ProviderState { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }

    public void Acquire(string leaseId, DateTimeOffset leaseExpiresAt)
    {
        Status = BillingLinkStatus.Pending;
        LeaseId = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        LastError = null;
        Touch();
    }

    public void Complete(SubscriptionConfirmation confirmation)
    {
        Status = BillingLinkStatus.Completed;
        ProductName = confirmation.ProductName;
        PriceInCents = confirmation.PriceInCents;
        ProviderState = confirmation.State;
        NextBillingDate = confirmation.NextBillingDate;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastError = null;
        Touch();
    }

    public void Fail(bool retryable, string safeError)
    {
        Status = retryable ? BillingLinkStatus.RetryableFailure : BillingLinkStatus.TerminalFailure;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastError = safeError;
        Touch();
    }

    public SubscriptionConfirmation? Confirmation()
    {
        if (Status != BillingLinkStatus.Completed || ProductName == null || PriceInCents == null || ProviderState == null)
        {
            return null;
        }

        return new SubscriptionConfirmation(
            SubscriptionReference,
            ProductHandle,
            ProductName,
            PriceInCents.Value,
            ProviderState,
            NextBillingDate);
    }

    private void Touch()
    {
        Version = Guid.NewGuid();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

