using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// App notifications that carry a provider SID and were created within the reconciliation window.
/// Used as the "what eShop believes it sent" side of the reconciliation report.
/// </summary>
public class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedDate >= from
                         && n.CreatedDate <= to);
    }
}
