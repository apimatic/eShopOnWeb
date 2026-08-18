using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every notification that carries a provider message identifier — i.e. everything eShop believes it
/// handed to the provider. Used as the eShop side of a reconciliation.
/// </summary>
public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}
