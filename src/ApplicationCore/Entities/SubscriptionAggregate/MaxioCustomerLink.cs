using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioCustomerLink
{
    private MaxioCustomerLink() { }

    public MaxioCustomerLink(string userId, string customerReference, int maxioCustomerId)
    {
        UserId = userId;
        CustomerReference = customerReference;
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = null!;
    public string CustomerReference { get; private set; } = null!;
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(int maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
