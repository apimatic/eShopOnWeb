using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification the provider actually created a message for (has a provider message id).</summary>
public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
