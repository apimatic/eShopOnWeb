using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription's plan change (UC3) is committed —
/// either immediately (prorated) or scheduled for the next renewal. See §2.5 of the integration plan.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference, int subscriptionId, string oldPlanHandle, string newPlanHandle, bool appliedImmediately, DateTimeOffset effectiveDate)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldPlanHandle = oldPlanHandle;
        NewPlanHandle = newPlanHandle;
        AppliedImmediately = appliedImmediately;
        EffectiveDate = effectiveDate;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string OldPlanHandle { get; }
    public string NewPlanHandle { get; }
    public bool AppliedImmediately { get; }
    public DateTimeOffset EffectiveDate { get; }
}
