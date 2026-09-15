using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification that was actually handed to the provider (has a message id) — the eShop side of reconciliation.</summary>
public class NotificationsWithProviderMessageSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderMessageSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
