using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendKeySpecification : Specification<NotificationResendKey>
{
    public NotificationResendKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(k => k.SourceNotificationId == sourceNotificationId && k.IdempotencyKey == idempotencyKey);
    }
}
