using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Complete(long maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RenewReservation()
    {
        MaxioSubscriptionId = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
