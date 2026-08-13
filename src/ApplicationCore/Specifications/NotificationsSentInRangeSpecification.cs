using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application believes it handed to the provider (they carry a provider
/// message id) whose creation falls within a date range. Used as the eShop side of reconciliation.
/// </summary>
public sealed class NotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= from
                         && n.CreatedAt <= to);
    }
}
