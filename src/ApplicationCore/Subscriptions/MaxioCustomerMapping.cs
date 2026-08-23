using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    private MaxioCustomerMapping() { }

    public MaxioCustomerMapping(string applicationUserId, string maxioCustomerReference)
    {
        ApplicationUserId = applicationUserId;
        MaxioCustomerReference = maxioCustomerReference;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public string MaxioCustomerReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public SubscriptionOperationStatus Status { get; private set; } = SubscriptionOperationStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void Confirm(int maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        Status = SubscriptionOperationStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkForReconciliation()
    {
        Status = SubscriptionOperationStatus.NeedsReconciliation;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
