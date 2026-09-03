using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CancellableOrderNotificationsByContactSpecification : Specification<OrderNotification>
{
    public CancellableOrderNotificationsByContactSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.ScheduledFor != null
            && n.ProviderMessageSid != null
            && n.CancellationCompletedAt == null);
    }
}
