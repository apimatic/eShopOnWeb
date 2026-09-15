using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendKeySpecification : Specification<NotificationResendKey>
{
    public NotificationResendKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(k => k.OriginalNotificationId == originalNotificationId && k.IdempotencyKey == idempotencyKey);
    }
}
