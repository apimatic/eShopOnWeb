using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it sent from the configured sending number within a date range —
/// the eShop side of the reconciliation. Only records the provider accepted (a SID is present) and
/// that went from the reconciled sending number are included; the scheduled follow-up, which goes
/// through the Messaging Service, is not from this number and is intentionally excluded.
/// </summary>
public class EShopSentNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public EShopSentNotificationsInRangeSpecification(string sendingNumber, DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.FromPhoneNumber == sendingNumber &&
            n.ProviderMessageSid != null &&
            n.CreatedDate >= from &&
            n.CreatedDate <= to);
    }
}
