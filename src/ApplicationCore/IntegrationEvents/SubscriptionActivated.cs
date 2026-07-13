using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription is successfully enrolled with the
/// billing provider (UC1). No durable broker/outbox — see plan.md §2.5.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string customerReference, int subscriptionId, string productHandle)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
}
