using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application actually handed to the provider and that were sent (they carry a
/// provider message identifier and a send time) within the reconciliation window. Used to line the
/// application's own record up against the provider's, which likewise keys on send time.
/// </summary>
public class NotificationsSentInRangeSpecification : Specification<Notification>
{
    public NotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.DateSent != null
            && n.DateSent >= from
            && n.DateSent <= to);
    }
}
