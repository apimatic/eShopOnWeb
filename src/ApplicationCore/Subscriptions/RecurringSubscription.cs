using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class RecurringSubscription : BaseEntity, IAggregateRoot
{
    private RecurringSubscription() { }

    public RecurringSubscription(
        string applicationUserId,
        string productHandle,
        string productName,
        long? priceInCents,
        string maxioSubscriptionReference)
    {
        ApplicationUserId = applicationUserId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        MaxioSubscriptionReference = maxioSubscriptionReference;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public long? PriceInCents { get; private set; }
    public string? Currency { get; private set; }
    public string? ProviderState { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public string MaxioSubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionOperationStatus OperationStatus { get; private set; } = SubscriptionOperationStatus.Pending;
    public bool SendStarted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void MarkSendStarted()
    {
        SendStarted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkForReconciliation()
    {
        OperationStatus = SubscriptionOperationStatus.NeedsReconciliation;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject()
    {
        OperationStatus = SubscriptionOperationStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Confirm(
        int maxioSubscriptionId,
        string productHandle,
        string productName,
        long? priceInCents,
        string? currency,
        string providerState,
        DateTimeOffset? nextBillingAt)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Currency = currency;
        ProviderState = providerState;
        NextBillingAt = nextBillingAt;
        OperationStatus = SubscriptionOperationStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
