using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, no durable outbox — plan.md §2.5) after UC1 successfully
/// enrolls a customer in a plan.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userId, int subscriptionId, string productHandle)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
}
