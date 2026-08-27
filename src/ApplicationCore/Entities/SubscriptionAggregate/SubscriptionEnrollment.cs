using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string CustomerReference { get; private set; }
    public string SubscriptionReference { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public bool SubscriptionWriteStarted { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }
#pragma warning restore CS8618

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        ProductHandle = Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Status = SubscriptionEnrollmentStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkPending()
    {
        Status = SubscriptionEnrollmentStatus.Pending;
        SubscriptionWriteStarted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkSubscriptionWriteStarted()
    {
        Status = SubscriptionEnrollmentStatus.Pending;
        SubscriptionWriteStarted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkSucceeded(int subscriptionId)
    {
        Status = SubscriptionEnrollmentStatus.Succeeded;
        MaxioSubscriptionId = subscriptionId;
        SubscriptionWriteStarted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkAmbiguous()
    {
        Status = SubscriptionEnrollmentStatus.Ambiguous;
        SubscriptionWriteStarted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkFailed()
    {
        Status = SubscriptionEnrollmentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
