using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications that carry a provider message identifier, used to reconcile against the provider.</summary>
public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
