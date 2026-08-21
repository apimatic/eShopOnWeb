using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreationStartedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionRecord() { }
    #pragma warning restore CS8618

    public SubscriptionRecord(string userId, string productHandle, string subscriptionReference)
    {
        UserId = Guard.Against.NullOrEmpty(userId, nameof(userId));
        ProductHandle = Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        SubscriptionReference = Guard.Against.NullOrEmpty(subscriptionReference, nameof(subscriptionReference));
        CreationStartedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(long maxioCustomerId, long maxioSubscriptionId)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
    }
}
