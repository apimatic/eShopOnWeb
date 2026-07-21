using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;


/// <summary>Published in-process after a customer is successfully enrolled in a plan (UC1).</summary>
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
