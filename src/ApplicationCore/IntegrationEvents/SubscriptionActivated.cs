using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a customer is successfully enrolled in a plan (UC1).
/// </summary>
public class SubscriptionActivated : INotification
{
    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }

    public SubscriptionActivated(string userReference, int subscriptionId, string productHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }
}
