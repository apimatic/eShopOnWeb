using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationIdempotencySpecification : Specification<NotificationIdempotencyRecord>
{
    public NotificationIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
