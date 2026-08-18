using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications that reached the provider (have a SID) and were created within a date range —
/// the "what eShop believes it sent" side of a reconciliation report.
/// </summary>
public class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
