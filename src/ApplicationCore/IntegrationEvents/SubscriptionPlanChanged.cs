using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a subscription's plan change is committed (UC3), immediate or delayed.</summary>
public class SubscriptionPlanChanged : INotification
{
    public int SubscriptionId { get; }
    public string UserName { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public bool AppliedImmediately { get; }
    public DateTimeOffset EffectiveAt { get; }

    public SubscriptionPlanChanged(int subscriptionId, string userName, string oldProductHandle, string newProductHandle, bool appliedImmediately, DateTimeOffset effectiveAt)
    {
        SubscriptionId = subscriptionId;
        UserName = userName;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        AppliedImmediately = appliedImmediately;
        EffectiveAt = effectiveAt;
    }
}
