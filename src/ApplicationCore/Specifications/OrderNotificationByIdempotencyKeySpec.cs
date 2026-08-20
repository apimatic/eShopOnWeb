using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByIdempotencyKeySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpec(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
