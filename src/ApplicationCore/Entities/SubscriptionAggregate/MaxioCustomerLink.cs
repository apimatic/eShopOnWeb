using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    private MaxioCustomerLink() { }

    public MaxioCustomerLink(string userId, string customerReference)
    {
        UserId = userId;
        CustomerReference = customerReference;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Connect(int maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
