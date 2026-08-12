using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application believes it handed to the provider (they carry a message SID) whose
/// creation falls in a date range. Used by reconciliation to line up what eShop thinks it sent against
/// what the provider actually has.
/// </summary>
public class OrderNotificationsWithProviderMessageInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderMessageInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
