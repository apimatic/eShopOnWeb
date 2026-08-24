using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string customerReference, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.InFlight;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Confirm(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = SubscriptionEnrollmentStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkUnresolved()
    {
        Status = SubscriptionEnrollmentStatus.Unresolved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionEnrollmentStatus
{
    InFlight,
    Confirmed,
    Unresolved
}
