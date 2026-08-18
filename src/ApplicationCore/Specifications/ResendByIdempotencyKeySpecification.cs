using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A resend already produced under a given idempotency key, if any — so a repeat does not send again.</summary>
public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.Type == NotificationType.Resend && n.IdempotencyKey == idempotencyKey);
    }
}
