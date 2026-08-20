using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class BillingCustomer : BaseEntity, IAggregateRoot
{
    private BillingCustomer() { }

    public BillingCustomer(string userId, long maxioCustomerId, string customerReference)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        CustomerReference = customerReference;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public long MaxioCustomerId { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Reconcile(long maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
    }
}
