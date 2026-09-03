using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByContactSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByContactSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id);
    }
}
