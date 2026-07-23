using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a customer was enrolled in a plan. Published in-process, best-effort, after the
/// provider call succeeds — handlers failing never rolls the subscription back.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string customerReference, int subscriptionId, string planHandle, decimal planPrice)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
        PlanPrice = planPrice;
    }

    public string CustomerReference { get; }

    public int SubscriptionId { get; }

    public string PlanHandle { get; }

    public decimal PlanPrice { get; }
}
