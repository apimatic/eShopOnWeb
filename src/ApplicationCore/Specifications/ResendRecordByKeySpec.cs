using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendRecordByKeySpec : Specification<NotificationResendRecord>, ISingleResultSpecification<NotificationResendRecord>
{
    public ResendRecordByKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.OriginalNotificationId == originalNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
