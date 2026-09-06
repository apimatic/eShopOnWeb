using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string Reference { get; private set; }
    public string ProductHandle { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Subscription(
        string userId,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string reference,
        string productHandle,
        string state,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        Reference = reference;
        ProductHandle = productHandle;
        State = state;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
    }
}
