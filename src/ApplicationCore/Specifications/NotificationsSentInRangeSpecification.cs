using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop handed to the provider (they carry a provider message id) whose creation
/// falls within a date range — the "what eShop believes it sent" side of a reconciliation.
/// </summary>
public class NotificationsSentInRangeSpecification : Specification<Notification>
{
    public NotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null &&
                         n.CreatedDate >= from &&
                         n.CreatedDate <= to);
    }
}
