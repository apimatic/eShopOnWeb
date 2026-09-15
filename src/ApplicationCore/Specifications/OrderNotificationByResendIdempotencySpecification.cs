using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendIdempotencySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByResendIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n =>
            n.ResentFromNotificationId == originalNotificationId &&
            n.IdempotencyKey == idempotencyKey);
    }
}
