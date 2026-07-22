using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription moves to a different plan. Delivery is best-effort.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference, int subscriptionId, string? previousPlanHandle,
        string newPlanHandle, PlanChangeTiming timing)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
    }

    public string UserReference { get; }

    public int SubscriptionId { get; }

    public string? PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }
}
