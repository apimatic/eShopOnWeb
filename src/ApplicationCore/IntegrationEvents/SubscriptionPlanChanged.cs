using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan (UC3). Published best-effort and
/// in-process after the provider call has succeeded.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription,
        string previousPlanHandle,
        PlanChangeTiming timing,
        PlanChangePreview preview)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Timing = timing;
        Preview = preview;
    }

    public Subscription Subscription { get; }

    public string PreviousPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The preview the customer confirmed, so handlers can report the amount that was applied.</summary>
    public PlanChangePreview Preview { get; }
}
