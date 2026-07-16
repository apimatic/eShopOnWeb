using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a customer is successfully enrolled in a plan (UC1).</summary>
public class SubscriptionActivated : INotification
{
    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }

    public SubscriptionActivated(string customerReference, int subscriptionId, string productHandle)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }
}
