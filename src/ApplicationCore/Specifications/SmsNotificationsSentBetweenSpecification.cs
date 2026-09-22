using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages whose PROVIDER send-time falls in a range — the same clock the
/// reconciliation report filters the provider side on (never the local row-creation time).
/// </summary>
public class SmsNotificationsSentBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsSentBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderDateSent != null
                         && n.ProviderDateSent >= from
                         && n.ProviderDateSent <= to);
    }
}
