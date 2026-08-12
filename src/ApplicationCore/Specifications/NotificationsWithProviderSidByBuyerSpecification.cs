using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications for a buyer that the provider actually accepted (have a provider SID).</summary>
public class NotificationsWithProviderSidByBuyerSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.ProviderMessageSid != null);
    }
}
