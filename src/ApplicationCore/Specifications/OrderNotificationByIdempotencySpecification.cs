using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByIdempotencySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
