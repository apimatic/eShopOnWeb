using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages that were handed to the provider (they carry a provider SID) and whose
/// send time falls within a reconciliation range. Lined up against the provider's own listing for the
/// same range so a message one side knows about and the other does not becomes visible.
/// </summary>
public class NotificationsWithProviderIdInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderIdInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.SentAt != null
            && n.SentAt >= from
            && n.SentAt <= to);
    }
}
