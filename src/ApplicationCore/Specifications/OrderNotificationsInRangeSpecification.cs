using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop notifications created within a date range, for reconciliation.</summary>
public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.CreatedAt);
    }
}
