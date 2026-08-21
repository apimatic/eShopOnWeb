using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByResendIdempotencySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationByResendIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
