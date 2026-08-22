using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencyKeySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendByIdempotencyKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == originalNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
