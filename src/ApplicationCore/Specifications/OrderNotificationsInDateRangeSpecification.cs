using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The messages this application believes it created within a date range, for lining up against
/// the provider's own record during reconciliation.
/// </summary>
public class OrderNotificationsInDateRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.CreatedAt);
    }
}
