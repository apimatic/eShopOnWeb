using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application actually handed to the provider (have a message id) whose send time
/// falls in the range — i.e. what eShop believes it sent, for reconciliation.
/// </summary>
public class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.SentAt != null
            && n.SentAt >= from
            && n.SentAt <= to);
    }
}
