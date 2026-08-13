using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it handed to the provider within a date range — those that got a
/// provider identifier back, created in [from, to]. Used as the eShop side of a reconciliation.
/// </summary>
public sealed class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedAt >= from &&
            n.CreatedAt <= to);
    }
}
