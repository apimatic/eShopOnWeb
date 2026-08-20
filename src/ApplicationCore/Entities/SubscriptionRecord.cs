using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local correlation data only. Maxio remains the source of truth for subscription state and pricing.
/// </summary>
public class SubscriptionRecord : BaseEntity
{
    private SubscriptionRecord()
    {
    }

    public SubscriptionRecord(string userId, string productHandle)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SynchronizedAtUtc { get; private set; }

    public void Synchronize(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        SynchronizedAtUtc = DateTimeOffset.UtcNow;
    }
}
