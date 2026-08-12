using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId)
             .OrderBy(n => n.CreatedAt);
    }
}
