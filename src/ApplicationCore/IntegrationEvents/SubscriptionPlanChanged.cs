using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort, §2.5) after a plan change is committed (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string buyerId, int subscriptionId, string oldProductHandle, string newProductHandle, long proratedAmountInCents, DateTimeOffset effectiveAt)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        ProratedAmountInCents = proratedAmountInCents;
        EffectiveAt = effectiveAt;
    }

    public string BuyerId { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public long ProratedAmountInCents { get; }
    public DateTimeOffset EffectiveAt { get; }
}
