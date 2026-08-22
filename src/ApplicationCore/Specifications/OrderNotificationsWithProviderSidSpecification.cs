using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
