using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local correlation record. Subscription details are always refreshed from Maxio.
/// </summary>
public class MaxioSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }

    private MaxioSubscription()
    {
        UserId = string.Empty;
        ProductHandle = string.Empty;
    }

    public MaxioSubscription(string userId, long maxioSubscriptionId, string productHandle)
    {
        UserId = userId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
    }
}
