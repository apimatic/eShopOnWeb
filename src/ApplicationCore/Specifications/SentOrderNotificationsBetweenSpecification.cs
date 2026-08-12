using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they carry a provider message id) that
/// were created within a date range — the eShop side of a reconciliation.
/// </summary>
public class SentOrderNotificationsBetweenSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= from
                         && n.CreatedAt <= to);
    }
}
