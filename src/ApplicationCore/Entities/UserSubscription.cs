using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;

    private UserSubscription() { }

    public UserSubscription(string userId, int maxioCustomerId, int maxioSubscriptionId, string productHandle, string state)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        State = state;
    }
}
