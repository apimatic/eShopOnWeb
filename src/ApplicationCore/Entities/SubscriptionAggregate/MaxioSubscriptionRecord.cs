using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring subscription in Maxio Advanced Billing, created on behalf of an
/// eShopOnWeb identity user. The Maxio subscription carries a deterministic
/// <c>reference</c> built from the user id and product handle so a repeated
/// signup can never produce two Maxio subscriptions.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity, IAggregateRoot
{
    public MaxioSubscriptionRecord(
        string userId,
        string maxioReference,
        int maxioSubscriptionId,
        string productHandle,
        string productName,
        long priceInCents,
        string state,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        MaxioReference = maxioReference;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string UserId { get; private set; }
    public string MaxioReference { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public long PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SyncFromMaxio(int maxioSubscriptionId, long priceInCents, string state,
        DateTimeOffset? nextBillingAt, DateTimeOffset updatedAt)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        UpdatedAt = updatedAt;
    }
}
