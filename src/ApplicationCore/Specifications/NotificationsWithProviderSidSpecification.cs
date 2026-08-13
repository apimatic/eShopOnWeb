using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every notification eShop has a provider identifier for — i.e. everything it believes it handed
/// to the provider. The date-range narrowing for reconciliation is applied by the caller.
/// </summary>
public class NotificationsWithProviderSidSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
