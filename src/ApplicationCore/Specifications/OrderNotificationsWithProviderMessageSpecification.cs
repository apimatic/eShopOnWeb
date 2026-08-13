using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All notifications the provider has issued a message SID for. Used as eShop's own side of a
/// reconciliation against the provider's record.
/// </summary>
public class OrderNotificationsWithProviderMessageSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderMessageSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null && n.ProviderMessageSid != string.Empty);
    }
}
