using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            ((n.ProviderDateSent != null && n.ProviderDateSent >= from && n.ProviderDateSent <= to) ||
             (n.ProviderDateSent == null && n.CreatedAt >= from && n.CreatedAt <= to)));
    }
}
