using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local correlation record. Maxio remains the source of truth for customer billing data.
/// </summary>
public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public long MaxioCustomerId { get; private set; }

    private MaxioCustomer()
    {
        UserId = string.Empty;
    }

    public MaxioCustomer(string userId, long maxioCustomerId)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
    }

    public void UpdateMaxioCustomerId(long maxioCustomerId) => MaxioCustomerId = maxioCustomerId;
}
