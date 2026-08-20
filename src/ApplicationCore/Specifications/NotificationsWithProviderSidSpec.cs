using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsWithProviderSidSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpec()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}
