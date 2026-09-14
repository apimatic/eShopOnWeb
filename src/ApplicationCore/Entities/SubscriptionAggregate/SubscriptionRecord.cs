using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SubscriptionRecord() { }
#pragma warning restore CS8618

    public SubscriptionRecord(string userId, string productHandle, string customerReference, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        LastAttemptAtUtc = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string CustomerReference { get; private set; }
    public string SubscriptionReference { get; private set; }
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public bool IsProvisioned { get; private set; }
    public DateTimeOffset LastAttemptAtUtc { get; private set; }

    public void MarkAttempt() => LastAttemptAtUtc = DateTimeOffset.UtcNow;

    public void MarkProvisioningFailed() => LastAttemptAtUtc = DateTimeOffset.MinValue;

    public void MarkProvisioned(long customerId, long subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        IsProvisioned = true;
        LastAttemptAtUtc = DateTimeOffset.UtcNow;
    }
}
