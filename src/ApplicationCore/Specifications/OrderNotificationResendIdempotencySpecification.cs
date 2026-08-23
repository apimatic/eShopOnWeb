using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationResendIdempotencySpecification : Specification<OrderNotification>, ISingleResultSpecification
{
    public OrderNotificationResendIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == sourceNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
