using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages it believes it handed to the provider (they carry a provider SID) within
/// a date range — the eShop side of a reconciliation.
/// </summary>
public sealed class OrderNotificationsSentBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderSid != null && n.CreatedDate >= from && n.CreatedDate <= to);
    }
}
