using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

public enum SubscriptionReservationStatus
{
    Reserved,
    CreateStarted,
    Completed,
    Failed
}

public sealed class SubscriptionReservation : BaseEntity, IAggregateRoot
{
    private SubscriptionReservation()
    {
    }

    public SubscriptionReservation(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionReservationStatus.Reserved;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string CustomerReference { get; private set; } = null!;
    public int? MaxioCustomerId { get; private set; }
    public string SubscriptionReference { get; private set; } = null!;
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordCustomer(int customerId)
    {
        MaxioCustomerId = customerId;
        Touch();
    }

    public void MarkCreateStarted()
    {
        Status = SubscriptionReservationStatus.CreateStarted;
        Touch();
    }

    public void MarkFailed()
    {
        Status = SubscriptionReservationStatus.Failed;
        Touch();
    }

    public void ResetForRetry()
    {
        if (Status != SubscriptionReservationStatus.Failed)
        {
            throw new InvalidOperationException("Only a definitively failed reservation can be retried.");
        }

        Status = SubscriptionReservationStatus.Reserved;
        Touch();
    }

    public void Complete(int subscriptionId)
    {
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionReservationStatus.Completed;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
