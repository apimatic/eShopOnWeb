using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's "believed sent" set for reconciliation: notifications that were handed to the provider (have
/// a message id) and were created within the range being reconciled.
/// </summary>
public class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
