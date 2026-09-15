using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}
