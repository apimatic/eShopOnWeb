using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userId, int subscriptionId, string fromProductHandle,
        string toProductHandle, decimal prorationAmount, DateTime effectiveDate)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        FromProductHandle = fromProductHandle;
        ToProductHandle = toProductHandle;
        ProrationAmount = prorationAmount;
        EffectiveDate = effectiveDate;
    }

    public string UserId { get; set; }
    public int SubscriptionId { get; set; }
    public string FromProductHandle { get; set; }
    public string ToProductHandle { get; set; }
    public decimal ProrationAmount { get; set; }
    public DateTime EffectiveDate { get; set; }
}
