using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notification produced by an operator re-send under a given idempotency key, so a repeat of the
/// same request can be recognised and not send a second message.
/// </summary>
public class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
