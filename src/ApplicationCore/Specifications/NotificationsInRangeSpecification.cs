using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications the provider accepted (they carry a provider message id), created in [from, to).
/// Used to line our records up against the provider's own list for reconciliation.
/// </summary>
public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null && n.CreatedAt >= from && n.CreatedAt < to);
    }
}
