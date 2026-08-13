using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it submitted to the provider (they carry a provider SID) within a
/// time range — the eShop side of a reconciliation.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<SmsNotification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
