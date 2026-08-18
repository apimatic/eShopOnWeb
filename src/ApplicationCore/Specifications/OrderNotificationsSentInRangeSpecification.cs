using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages that the provider accepted (have a SID) whose creation falls within
/// a range &ndash; the eShop side of the reconciliation.
/// </summary>
public class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
