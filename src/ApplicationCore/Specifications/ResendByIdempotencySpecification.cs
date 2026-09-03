using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == sourceNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
