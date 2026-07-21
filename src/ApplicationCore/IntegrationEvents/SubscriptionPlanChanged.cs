using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a plan change (upgrade/downgrade) is committed (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string fromPlanHandle, string toPlanHandle, DateTimeOffset effectiveDate)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        FromPlanHandle = fromPlanHandle;
        ToPlanHandle = toPlanHandle;
        EffectiveDate = effectiveDate;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string FromPlanHandle { get; }
    public string ToPlanHandle { get; }
    public DateTimeOffset EffectiveDate { get; }
}
