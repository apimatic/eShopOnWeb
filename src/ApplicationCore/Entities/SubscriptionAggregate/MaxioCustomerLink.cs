using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    private MaxioCustomerLink() { }

    public MaxioCustomerLink(
        string userId,
        string customerReference,
        int maxioCustomerId,
        DateTimeOffset now)
    {
        UserId = userId;
        CustomerReference = customerReference;
        MaxioCustomerId = maxioCustomerId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(int maxioCustomerId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = now;
    }
}
