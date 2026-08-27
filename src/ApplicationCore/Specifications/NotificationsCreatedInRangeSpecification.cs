using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications created within the given range that carry a provider message
/// identifier, i.e. the messages eShop believes it sent.
/// </summary>
public class NotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
