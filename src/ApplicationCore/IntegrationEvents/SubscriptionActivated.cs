using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userId, int subscriptionId, string productHandle, decimal price, DateTime nextBillingDate)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
        Price = price;
        NextBillingDate = nextBillingDate;
    }

    public string UserId { get; set; }
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; }
    public decimal Price { get; set; }
    public DateTime NextBillingDate { get; set; }
}
