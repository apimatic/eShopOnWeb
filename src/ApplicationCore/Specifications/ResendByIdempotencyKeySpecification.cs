using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
