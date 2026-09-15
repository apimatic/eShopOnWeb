using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationByIdempotencySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId
                         && n.Kind == NotificationKind.Resend
                         && n.IdempotencyKey == idempotencyKey);
    }
}
