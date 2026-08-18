using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application submitted to the provider within a range — i.e. those it
/// believes it sent — for lining up against the provider's own record during reconciliation.
/// </summary>
public class OrderNotificationsSubmittedBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSubmittedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.SubmittedAt != null && n.SubmittedAt >= from && n.SubmittedAt <= to);
    }
}
