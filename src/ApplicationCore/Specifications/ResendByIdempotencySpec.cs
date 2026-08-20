using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendByIdempotencySpec(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ParentNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
