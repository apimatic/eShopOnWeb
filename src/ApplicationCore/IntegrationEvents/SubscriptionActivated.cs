using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

// Published in-process, best-effort, after a subscription is successfully enrolled (UC1).
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string customerReference, int subscriptionId, string planHandle)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string PlanHandle { get; }
}
