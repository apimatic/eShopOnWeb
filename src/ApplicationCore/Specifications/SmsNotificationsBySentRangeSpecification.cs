using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application believes it sent within a date range — those that carry a
/// provider message identifier and whose send time falls in <c>[from, to]</c>. This is the
/// "what eShop believes it sent" side of the reconciliation report.
/// </summary>
public sealed class SmsNotificationsBySentRangeSpecification : Specification<SmsNotification>
{
    public SmsNotificationsBySentRangeSpecification(DateTimeOffset fromInclusive, DateTimeOffset toInclusive)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.SentAt != null
                         && n.SentAt >= fromInclusive
                         && n.SentAt <= toInclusive);
    }
}
