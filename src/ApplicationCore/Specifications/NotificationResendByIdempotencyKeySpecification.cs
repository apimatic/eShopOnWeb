using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByIdempotencyKeySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationResendByIdempotencyKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId
                         && n.ResendIdempotencyKey == idempotencyKey
                         && n.Kind == OrderNotificationKind.Resend);
    }
}
