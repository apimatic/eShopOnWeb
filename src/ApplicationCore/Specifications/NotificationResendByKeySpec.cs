using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByKeySpec : Specification<NotificationResendRecord>, ISingleResultSpecification<NotificationResendRecord>
{
    public NotificationResendByKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(record =>
            record.OriginalNotificationId == originalNotificationId &&
            record.IdempotencyKey == idempotencyKey);
    }
}
