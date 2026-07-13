using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort) after a subscription is successfully activated with the billing provider.</summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, int subscriptionId, string productHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
}
