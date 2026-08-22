using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveNotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public ActiveNotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderSid != null);
    }
}
