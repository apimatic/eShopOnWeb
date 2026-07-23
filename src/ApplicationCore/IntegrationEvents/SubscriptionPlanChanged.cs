using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a plan change is committed (UC3 step 5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription, string previousPlanHandle, PlanChangePreview preview)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Preview = preview;
    }

    public Subscription Subscription { get; }

    public string PreviousPlanHandle { get; }

    /// <summary>The preview the customer confirmed, so handlers can report the agreed amount.</summary>
    public PlanChangePreview Preview { get; }
}
