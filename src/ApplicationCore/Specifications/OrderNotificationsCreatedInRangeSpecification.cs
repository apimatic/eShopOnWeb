using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => (n.ProviderCreatedAt ?? n.CreatedAt) >= from
                && (n.ProviderCreatedAt ?? n.CreatedAt) <= to)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id);
    }
}
