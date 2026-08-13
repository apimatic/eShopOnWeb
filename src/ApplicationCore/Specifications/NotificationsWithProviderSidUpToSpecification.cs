using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every notification that carries a provider message identifier and was created no later than
/// <paramref name="to"/>. Used to match the provider's messages against the application's records by
/// identifier, independent of exactly when each was sent.
/// </summary>
public class NotificationsWithProviderSidUpToSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidUpToSpecification(DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt <= to);
    }
}
