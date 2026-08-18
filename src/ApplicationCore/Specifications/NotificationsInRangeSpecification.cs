using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages it handed to the provider within a range (has a provider SID and was
/// created in the window) — the eShop side of a reconciliation.
/// </summary>
public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to)
            .AsNoTracking()
            .OrderBy(n => n.CreatedAt);
    }
}
