using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved between plans. Published in-process, best-effort, after the
/// provider call succeeds.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string oldPlanHandle,
        string newPlanHandle, PlanChangeTiming timing)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldPlanHandle = oldPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
    }

    public string CustomerReference { get; }

    public int SubscriptionId { get; }

    public string OldPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }
}
