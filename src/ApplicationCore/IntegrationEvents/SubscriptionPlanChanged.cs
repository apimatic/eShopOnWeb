using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription has moved to a different plan (UC3, step 5).
/// Delivery is best-effort: a failing handler never rolls back the plan change.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference,
        int subscriptionId,
        string? previousPlanHandle,
        string newPlanHandle,
        PlanChangeTiming timing,
        PlanChangePreview appliedPreview,
        CustomerSubscription subscription)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
        AppliedPreview = appliedPreview;
        Subscription = subscription;
    }

    public string UserReference { get; }

    public int SubscriptionId { get; }

    public string? PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The preview whose amounts the customer confirmed and which was actually applied.</summary>
    public PlanChangePreview AppliedPreview { get; }

    public CustomerSubscription Subscription { get; }
}
