using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendRecordByKeySpec : Specification<NotificationResendRecord>
{
    public ResendRecordByKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.OriginalNotificationId == originalNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
