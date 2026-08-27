using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByKeySpecification : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpecification(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
