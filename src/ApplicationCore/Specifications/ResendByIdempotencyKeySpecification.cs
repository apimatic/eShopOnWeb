using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
