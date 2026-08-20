using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendIdempotencySpec : Specification<NotificationResendIdempotency>
{
    public ResendIdempotencySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(x => x.OriginalNotificationId == originalNotificationId && x.IdempotencyKey == idempotencyKey);
    }
}
