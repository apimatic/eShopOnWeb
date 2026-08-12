using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application believes it handed to the provider (they carry a provider message
/// id) within a time window — the eShop side of a reconciliation.
/// </summary>
public class NotificationsWithProviderSidBetweenSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedDate >= from && n.CreatedDate <= to);
    }
}
