using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendIdempotencySpec : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencySpec(int notificationId, string idempotencyKey)
    {
        Query.Where(r => r.NotificationId == notificationId && r.IdempotencyKey == idempotencyKey);
    }
}
