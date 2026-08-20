using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendAttemptSpec : Specification<NotificationResendAttempt>, ISingleResultSpecification<NotificationResendAttempt>
{
    public NotificationResendAttemptSpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(a => a.SourceNotificationId == sourceNotificationId && a.IdempotencyKey == idempotencyKey);
    }
}
