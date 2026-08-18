using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's records of messages that reached the provider (have a SID) within a date range — the eShop side of
/// a reconciliation.
/// </summary>
public class SentNotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public SentNotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedDate >= from &&
            n.CreatedDate <= to);
    }
}
