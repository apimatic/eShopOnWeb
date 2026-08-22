using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendAttemptSpecification : Specification<NotificationResendAttempt>
{
    public NotificationResendAttemptSpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(a => a.SourceNotificationId == sourceNotificationId && a.IdempotencyKey == idempotencyKey);
    }
}
