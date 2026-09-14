using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioCustomerLink
{
    private MaxioCustomerLink()
    {
    }

    public MaxioCustomerLink(string userId, int maxioCustomerId, string customerReference)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        CustomerReference = customerReference;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(int maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
