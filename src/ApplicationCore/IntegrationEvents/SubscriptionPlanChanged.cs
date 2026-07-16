using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a subscription's plan change is committed (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string FromProductHandle { get; }
    public string ToProductHandle { get; }
    public DateTimeOffset EffectiveAt { get; }

    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string fromProductHandle, string toProductHandle, DateTimeOffset effectiveAt)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        FromProductHandle = fromProductHandle;
        ToProductHandle = toProductHandle;
        EffectiveAt = effectiveAt;
    }
}
