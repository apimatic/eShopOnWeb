using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop's own notification records created within a date range, for reconciliation.</summary>
public sealed class OrderNotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
