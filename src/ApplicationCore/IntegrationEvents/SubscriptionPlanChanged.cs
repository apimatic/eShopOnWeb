using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan. Published in-process, best-effort, only after
/// the provider has committed the change.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(PlanChangeResult result)
    {
        Result = result;
    }

    public PlanChangeResult Result { get; }

    public int SubscriptionId => Result.Subscription.Id;

    public string? PreviousPlanHandle => Result.PreviousPlanHandle;

    public string TargetPlanHandle => Result.TargetPlanHandle;
}
