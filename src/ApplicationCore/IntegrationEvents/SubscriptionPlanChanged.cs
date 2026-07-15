using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

// Published in-process, best-effort, after a plan change (UC3) is committed (either timing).
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string oldPlanHandle,
        string newPlanHandle, bool effectiveImmediately)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldPlanHandle = oldPlanHandle;
        NewPlanHandle = newPlanHandle;
        EffectiveImmediately = effectiveImmediately;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string OldPlanHandle { get; }
    public string NewPlanHandle { get; }
    public bool EffectiveImmediately { get; }
}
