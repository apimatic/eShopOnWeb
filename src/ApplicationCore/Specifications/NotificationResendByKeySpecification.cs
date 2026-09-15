using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByKeySpecification : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.OriginalNotificationId == originalNotificationId
                         && r.IdempotencyKey == idempotencyKey);
    }
}
