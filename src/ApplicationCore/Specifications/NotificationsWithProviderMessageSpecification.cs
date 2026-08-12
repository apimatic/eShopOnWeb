using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications that were handed to the provider (they carry a provider message id) — the eShop side
/// of a reconciliation against the provider's own record.
/// </summary>
public sealed class NotificationsWithProviderMessageSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderMessageSpecification()
    {
        Query.Where(n => n.ProviderMessageId != null);
    }
}
