using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
