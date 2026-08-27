using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public NotificationsByOwnerSpecification(string ownerId)
    {
        Query
            .Where(n => n.OwnerId == ownerId)
            .OrderBy(n => n.Id);
    }
}
